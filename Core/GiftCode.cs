using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

[Serializable]
public class GiftItem
{
    public string id;
    public int count = 1;
}

/// <summary>補填コードの中身。コードそのものに、内容が入っている（ゲーム側に、コードの一覧は持たない）。</summary>
[Serializable]
public class GiftData
{
    public int v = 1;
    /// <summary>コードの ID（同じセーブデータでは、1回しか使えない）</summary>
    public string id;
    public string memo;
    public int gold, crystals, mako;
    /// <summary>上がるレベル</summary>
    public int addLevels;
    /// <summary>魔法石（属性ごと。ElementUtil.All の順）</summary>
    public int[] stones = new int[6];
    public List<GiftItem> items = new List<GiftItem>();
    /// <summary>素材（Materials の ID）</summary>
    public List<GiftItem> materials = new List<GiftItem>();
    /// <summary>クリア済みにするダンジョンの ID（クリア進捗を戻す）</summary>
    public List<string> clearedDungeons = new List<string>();
    /// <summary>true なら、すべてのダンジョンを、クリア済みにする</summary>
    public bool clearAllDungeons;
    /// <summary>璋喚玉</summary>
    public int orbs;
    /// <summary>召喚獣（PetData の ID と、匹数）</summary>
    public List<GiftItem> pets = new List<GiftItem>();
}

/// <summary>
/// 補填コード。セーブコードと同じように、内容を JSON → 圧縮 → Base64 にした文字列で、そこから情報を読み取って、加える。
/// 偽造を防ぐため、末尾に、内容の署名（HMAC）が付く。使ったコードの ID は、セーブデータに記録され、同じセーブデータでは、もう使えない。
/// Dungeon > Create Gift Code（Editor）で作る。
/// </summary>
public static class GiftCode
{
    const string Prefix = "DGGIFT1:";

    // 署名の鍵。変えると、これまでに作ったコードは、すべて使えなくなる（ゲームを公開するなら、自分だけの値に変えること）
    const string Secret = "REDACTED";

    static string Sign(string payload)
    {
        using (var h = new HMACSHA256(Encoding.UTF8.GetBytes(Secret)))
        {
            var mac = h.ComputeHash(Encoding.UTF8.GetBytes(payload));
            var sb = new StringBuilder();
            for (int i = 0; i < 10; i++) sb.Append(mac[i].ToString("x2"));
            return sb.ToString();
        }
    }

    /// <summary>新しいコード ID（8文字）。</summary>
    public static string NewId() => Guid.NewGuid().ToString("N").Substring(0, 8).ToUpperInvariant();

    public static string Encode(GiftData data)
    {
        string json = JsonUtility.ToJson(data);
        byte[] raw = Encoding.UTF8.GetBytes(json);
        using (var ms = new MemoryStream())
        {
            using (var gz = new GZipStream(ms, System.IO.Compression.CompressionLevel.Optimal, true)) gz.Write(raw, 0, raw.Length);
            string b64 = Convert.ToBase64String(ms.ToArray());
            return Prefix + b64 + "." + Sign(b64);
        }
    }

    public static bool TryParse(string code, out GiftData data, out string error)
    {
        data = null;
        error = null;
        if (string.IsNullOrWhiteSpace(code)) { error = "コードを入力してください。"; return false; }
        var sb = new StringBuilder();
        foreach (char c in code) if (!char.IsWhiteSpace(c)) sb.Append(c);
        string s = sb.ToString();
        if (!s.StartsWith(Prefix)) { error = "補填コードの形式が正しくありません。"; return false; }
        s = s.Substring(Prefix.Length);
        int dot = s.LastIndexOf('.');
        if (dot < 0) { error = "補填コードの形式が正しくありません。"; return false; }
        string b64 = s.Substring(0, dot), sig = s.Substring(dot + 1);
        if (!string.Equals(Sign(b64), sig, StringComparison.OrdinalIgnoreCase)) { error = "補填コードが正しくありません（途中で切れている、または書き換えられています）。"; return false; }
        try
        {
            byte[] bytes = Convert.FromBase64String(b64);
            using (var input = new MemoryStream(bytes))
            using (var gz = new GZipStream(input, CompressionMode.Decompress))
            using (var output = new MemoryStream())
            {
                gz.CopyTo(output);
                data = JsonUtility.FromJson<GiftData>(Encoding.UTF8.GetString(output.ToArray()));
            }
            if (data == null || string.IsNullOrEmpty(data.id)) { error = "補填コードを読み取れませんでした。"; data = null; return false; }
            return true;
        }
        catch (Exception)
        {
            error = "補填コードが壊れています。";
            data = null;
            return false;
        }
    }

    /// <summary>内容の説明（コードを作るときの確認・受け取ったあとの表示用）。</summary>
    public static string Summarize(GiftData d, System.Func<string, string> itemName = null)
    {
        var lines = new List<string>();
        var db = ItemDatabase.Instance;
        if (d.addLevels > 0) lines.Add($"レベル +{d.addLevels}");
        if (d.gold > 0) lines.Add($"{d.gold:N0} G");
        if (d.crystals > 0) lines.Add($"魔水晶 ×{d.crystals}");
        if (d.mako > 0) lines.Add($"魔煌珠 ×{d.mako}");
        if (d.orbs > 0) lines.Add($"璋喚玉 ×{d.orbs}");
        if (d.pets != null)
            foreach (var gp in d.pets)
            {
                if (gp == null || string.IsNullOrEmpty(gp.id)) continue;
                var pd = PetCatalog.Find(gp.id);
                lines.Add($"召喚獣 {(pd != null ? pd.displayName : gp.id + "（不明）")} ×{gp.count}");
            }
        if (d.clearAllDungeons) lines.Add("すべてのダンジョンを、クリア済みにする");
        else if (d.clearedDungeons != null && d.clearedDungeons.Count > 0) lines.Add($"ダンジョンのクリア進捗 {d.clearedDungeons.Count} か所");
        var els = ElementUtil.All;
        for (int i = 0; i < els.Length && d.stones != null && i < d.stones.Length; i++)
            if (d.stones[i] > 0) lines.Add($"魔法石（{ElementUtil.Name(els[i])}） ×{d.stones[i]}");
        foreach (var gi in d.items)
        {
            if (gi == null || string.IsNullOrEmpty(gi.id)) continue;
            string nm = itemName != null ? itemName(gi.id) : null;
            if (nm == null) { var it = db != null ? db.Find(gi.id) : null; nm = it != null ? it.displayName : null; }
            lines.Add($"{(nm != null ? nm : gi.id + "（不明）")} ×{gi.count}");
        }
        foreach (var gm in d.materials)
        {
            if (gm == null || string.IsNullOrEmpty(gm.id)) continue;
            var def = Materials.Find(gm.id);
            lines.Add($"{(def != null ? def.name : gm.id + "（不明）")} ×{gm.count}");
        }
        return lines.Count == 0 ? "（中身なし）" : string.Join("\n", lines);
    }

    /// <summary>ゲームの中で、コードを使う。成功したら true。message は、結果の説明（失敗なら理由）。</summary>
    public static bool Redeem(string code, out string message)
    {
        message = null;
        if (GameSession.RunActive) { message = "補填コードは、ホームでのみ使えます。"; return false; }
        if (!TryParse(code, out var d, out message)) return false;
        if (GameSession.IsGiftUsed(d.id)) { message = "このコードは、このセーブデータでは、すでに使用済みです。"; return false; }
        var db = ItemDatabase.Instance;
        if (db == null) { message = "ItemDatabase が見つかりません。"; return false; }

        var lines = new List<string>();
        if (d.addLevels > 0)
        {
            int before = GameSession.Level;
            GameSession.GrantLevels(d.addLevels);
            lines.Add($"レベル {before} → {GameSession.Level}");
        }
        // ダンジョンのクリア進捗（クリア済みにする。初回クリアの報酬は出ない）
        if (d.clearAllDungeons || (d.clearedDungeons != null && d.clearedDungeons.Count > 0))
        {
            int added = 0;
            foreach (var dd in DungeonSession.All())
            {
                if (dd == null || dd.tutorial || dd.isInfinite) continue;
                if (!d.clearAllDungeons && !d.clearedDungeons.Contains(dd.Id)) continue;
                if (GameSession.GrantClear(dd)) added++;
            }
            lines.Add(added > 0 ? $"ダンジョンのクリア進捗 {added} か所" : "ダンジョンのクリア進捗（すでにクリア済み）");
        }
        if (d.gold > 0) { GameSession.GrantGold(d.gold); lines.Add($"{d.gold:N0} G"); }
        if (d.crystals > 0) { GameSession.AddCrystal(d.crystals); lines.Add($"魔水晶 ×{d.crystals}"); }
        if (d.mako > 0) { GameSession.AddMako(d.mako); lines.Add($"魔煌珠 ×{d.mako}"); }
        if (d.orbs > 0) { PetSystem.AddOrbs(d.orbs); lines.Add($"璋喚玉 ×{d.orbs}"); }
        if (d.pets != null)
            foreach (var gp in d.pets)
            {
                if (gp == null || string.IsNullOrEmpty(gp.id) || gp.count <= 0) continue;
                var pd = PetCatalog.Find(gp.id);
                if (pd == null) { lines.Add($"<color=#ff9a8a>不明な召喚獣（{gp.id}）は、受け取れなかった</color>"); continue; }
                for (int k = 0; k < gp.count; k++) PetSystem.Create(pd);
                lines.Add($"召喚獣 {pd.displayName} ×{gp.count}（牧場へ）");
            }
        var els = ElementUtil.All;
        for (int i = 0; i < els.Length && d.stones != null && i < d.stones.Length; i++)
            if (d.stones[i] > 0) { GameSession.AddStone(els[i], d.stones[i]); lines.Add($"魔法石（{ElementUtil.Name(els[i])}） ×{d.stones[i]}"); }

        foreach (var gi in d.items)
        {
            if (gi == null || string.IsNullOrEmpty(gi.id) || gi.count <= 0) continue;
            var item = db.Find(gi.id);
            if (item == null) { lines.Add($"<color=#ff9a8a>不明なアイテム（{gi.id}）は、受け取れなかった</color>"); continue; }
            int bag = 0, stored = 0, lost = 0;
            for (int k = 0; k < gi.count; k++)
            {
                if (GameSession.Add(item, false) != null) bag++;
                else if (GameSession.GiveOrStore(item)) stored++;
                else lost++;
            }
            string s = $"{item.displayName} ×{gi.count}";
            if (stored > 0) s += $"（{stored}個は、持ちきれないので、ホームの大きな宝箱へ）";
            if (lost > 0) s += $"<color=#ff9a8a>（{lost}個は、宝箱もいっぱいで、受け取れなかった）</color>";
            lines.Add(s);
        }
        foreach (var gm in d.materials)
        {
            if (gm == null || string.IsNullOrEmpty(gm.id) || gm.count <= 0) continue;
            var def = Materials.Find(gm.id);
            if (def == null) { lines.Add($"<color=#ff9a8a>不明な素材（{gm.id}）は、受け取れなかった</color>"); continue; }
            int got = Materials.AddForce(gm.id, gm.count);
            lines.Add($"{def.name} ×{got}");
        }

        GameSession.MarkGiftUsed(d.id);
        SaveSystem.Save();
        message = "補填コードを使いました。\n" + string.Join("\n", lines);
        return true;
    }
}

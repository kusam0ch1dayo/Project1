using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using UnityEngine;

/// <summary>所持品・装備・探索回数を JSON でセーブ/ロードする。保存先: Application.persistentDataPath/save.json</summary>
public static class SaveSystem
{
    [Serializable]
    public class SaveData
    {
        public int version = 2;
        // 旧形式（id のみ）。読み込みだけに使う。
        public List<string> bag = new List<string>();
        public string[] equipped = new string[0];
        // 現行形式（お気に入り・覚醒・レベルなどの個別の状態つき）
        public List<ItemSave> bagItems = new List<ItemSave>();
        public List<ItemSave> equippedItems = new List<ItemSave>();
        public int runs;
        public int survived;
        public int gold;
        public int crystals;
        public int mako;                                            // 魔煌珠
        public List<string> skillUnlocked = new List<string>();     // 魔煌珠で解放した、選ばなかったブランチ
        public int[] stones = new int[6];
        /// <summary>素材の所持（"id:個数"）</summary>
        public string[] materials = new string[0];
        public bool night;
        public string[] quickSlots = new string[3];
        public int level = 1;
        public float exp;
        /// <summary>クリアしたダンジョンのID</summary>
        public List<string> clearedDungeons = new List<string>();
        /// <summary>HP の割合（0〜1）。-1 なら満タン（セーブに HP が無い古いデータ）。</summary>
        public float hpRatio = -1f;
        public List<string> discovered = new List<string>();
        /// <summary>随時入荷装備（売り切れは空文字）。空リストなら未入荷。</summary>
        public List<string> shopStock = new List<string>();
        /// <summary>図鑑の登録済みキー</summary>
        public List<string> codex = new List<string>();
        /// <summary>ホームの大きな宝箱に預けているアイテム</summary>
        public List<ItemSave> storageItems = new List<ItemSave>();
        /// <summary>図鑑の報酬を受け取った件数</summary>
        public int codexClaimed;
        /// <summary>チュートリアルをまだ遊んでいない（新しいセーブだけ true。古いセーブには無いので false = 出さない）。</summary>
        public bool tutorialPending;
        /// <summary>村のクエスト（依頼主ごとに1件。受注中・達成済みの状態を含む）</summary>
        public List<QuestData> questBoard = new List<QuestData>();
        /// <summary>スキルツリーで取ったノードのID（オリジン・ブランチ・パワーストーン）</summary>
        public List<string> skillNodes = new List<string>();
        /// <summary>スキルのスロット（1・2・3）に設定しているスキル</summary>
        public string[] skillSlots = new string[3];
        /// <summary>難易度（0: イージー / 1: ノーマル / 2: ハード）と、選んだかどうか（古いセーブは false = 最初に聞く）</summary>
        public int difficulty = 1;
        public bool difficultyChosen;
        /// <summary>追加アイテム（たいまつ）の初期配布を済ませた（古いセーブは false = 1つ渡す）。</summary>
        public bool extrasGiven;
        /// <summary>侵食しているダンジョン（"ID|レベルの上乗せ|モディファイヤ"）と、侵食を鎮めたダンジョンのID</summary>
        public List<string> corruptedDungeons = new List<string>();
        public List<string> purifiedDungeons = new List<string>();
        /// <summary>解放したレシピのID</summary>
        public List<string> unlockedRecipes = new List<string>();
        /// <summary>使った補填コードの ID（このセーブデータでは、もう使えない）</summary>
        public List<string> usedGifts = new List<string>();
        /// <summary>中断中の無限ダンジョン（JSON。空なら中断なし）。再読み込みしても続きから遊べる。</summary>
        public string infiniteRun = "";
        /// <summary>釣り人から、釣り竿を借りた（1回だけ）。</summary>
        public bool fishingRodLent;
        /// <summary>魚図鑑の記録（"id|匹数|最大cm|最小cm"）</summary>
        public List<string> fishRecords = new List<string>();
        /// <summary>実績: 記録（"種類=値"）・解除した実績の id・報酬を受け取った実績の id</summary>
        public List<string> achievementStats = new List<string>();
        public List<string> achievementsDone = new List<string>();
        public List<string> achievementsClaimed = new List<string>();
        /// <summary>召喚獣（ペット）・璋喚玉・ガチャの天井</summary>
        public PetSaveBlock pets = new PetSaveBlock();
    }

    public static string FilePath => Path.Combine(Application.persistentDataPath, "save.json");

    /// <summary>現在の状態を SaveData にまとめる。</summary>
    public static SaveData BuildData()
    {
        var data = new SaveData { runs = GameSession.Runs, survived = GameSession.Survived, gold = GameSession.Gold, night = TimeOfDay.IsNight };
        data.hpRatio = GameSession.HpRatio;
        data.difficulty = (int)Difficulty.Current;
        data.difficultyChosen = Difficulty.Chosen;
        data.extrasGiven = true;
        data.corruptedDungeons = Corruption.ExportCorrupted();
        data.purifiedDungeons = Corruption.ExportPurified();
        data.unlockedRecipes = RecipeBook.ExportUnlocked();
        data.usedGifts = new List<string>(GameSession.UsedGifts);
        data.infiniteRun = Infinite.ExportSuspended();
        data.fishingRodLent = GameSession.FishingRodLent;
        data.fishRecords = FishBook.Export();
        data.achievementStats = Achievements.ExportStats();
        data.achievementsDone = Achievements.ExportDone();
        data.achievementsClaimed = Achievements.ExportClaimed();
        data.pets = PetSystem.Export();
        data.quickSlots = (string[])GameSession.QuickSlotIds.Clone();
        data.level = GameSession.Level;
        data.exp = GameSession.Exp;
        data.clearedDungeons.AddRange(GameSession.ClearedDungeons);
        data.discovered.AddRange(GameSession.Discovered);
        data.shopStock = ShopSystem.StockIds();
        data.codex.AddRange(Codex.Entries);
        data.codexClaimed = Codex.ClaimedCount;
        data.tutorialPending = TutorialSession.Pending;
        data.questBoard = new List<QuestData>(QuestSystem.Board);
        data.skillNodes = SkillTree.TakenIds();
        data.skillSlots = SkillSystem.ExportSlots();
        data.crystals = GameSession.Crystals;
        data.mako = GameSession.Mako;
        data.skillUnlocked = SkillTree.UnlockedIds();
        data.stones = new int[ElementUtil.StoneSlots];
        foreach (var e in ElementUtil.All) data.stones[(int)e] = GameSession.GetStones(e);
        data.materials = Materials.Export();

        foreach (var it in GameSession.Bag)
            if (it != null) data.bagItems.Add(it.ToSave());
        data.bagItems.AddRange(GameSession.Orphans); // データベースに無かったアイテムも、そのまま残す
        foreach (var it in GameSession.Storage) if (it != null) data.storageItems.Add(it.ToSave());
        data.storageItems.AddRange(GameSession.StorageOrphans);
        for (int i = 0; i < GameSession.SlotCount; i++)
        {
            var eq = GameSession.GetEquipped((EquipSlot)i);
            if (eq != null) data.equippedItems.Add(eq.ToSave());
        }
        return data;
    }

    public static bool FileExists => File.Exists(FilePath);

    public static void Save()
    {
        // 読み込む前・読み込みに失敗したあとは、保存しない（空のデータで上書きしてしまうため）
        if (!GameSession.SaveAllowed) return;
        if (Infinite.Active) return; // 無限ダンジョンの最中は、没収された状態を、保存しない
        try
        {
            WriteFile(BuildData());
        }
        catch (Exception e)
        {
            Debug.LogError("セーブに失敗しました: " + e.Message);
        }
    }

#if UNITY_WEBGL && !UNITY_EDITOR
    [System.Runtime.InteropServices.DllImport("__Internal")] static extern void JS_FileSystem_Sync();
#endif

    /// <summary>一時ファイルに書いてから入れ替える（書き込み中に止まっても、元のセーブが壊れない）。</summary>
    public static void WriteFile(SaveData data)
    {
        string tmp = FilePath + ".tmp";
        File.WriteAllText(tmp, JsonUtility.ToJson(data, true));
        if (File.Exists(FilePath)) File.Delete(FilePath);
        File.Move(tmp, FilePath);
#if UNITY_WEBGL && !UNITY_EDITOR
        try { JS_FileSystem_Sync(); } catch (System.Exception) { }   // ブラウザの保存領域（IndexedDB）へ、すぐ反映する（タブを閉じても残る）
#endif
    }

    static bool backedUp;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetBackup() => backedUp = false;

    /// <summary>いまのセーブファイルを、起動ごとに1回だけ save_backup_日時.json として控える（直近5個を残す）。</summary>
    public static void BackupOnce()
    {
        if (backedUp) return;
        backedUp = true;
        try
        {
            if (!File.Exists(FilePath)) return;
            string dir = Path.GetDirectoryName(FilePath);
            File.Copy(FilePath, Path.Combine(dir, $"save_backup_{DateTime.Now:yyyyMMdd_HHmmss}.json"), true);

            var olds = new List<string>(Directory.GetFiles(dir, "save_backup_*.json"));
            olds.Sort(StringComparer.Ordinal);
            for (int i = 0; i < olds.Count - 5; i++) File.Delete(olds[i]);
        }
        catch (Exception e)
        {
            Debug.LogWarning("セーブのバックアップに失敗しました: " + e.Message);
        }
    }

    // ---------------------------------------------------------------- セーブコード（複数のデータを文字列で管理する）

    const string CodePrefix = "DGSAVE1:";

    /// <summary>現在のデータを、コピーして保管できる文字列（セーブコード）にする。JSON を圧縮して Base64 にしたもの。</summary>
    public static string ExportCode()
    {
        var exp = BuildData();
        exp.infiniteRun = ""; // 中断データは、セーブコードに含めない（長くなるため）
        string json = JsonUtility.ToJson(exp);
        byte[] raw = Encoding.UTF8.GetBytes(json);
        using (var ms = new MemoryStream())
        {
            using (var gz = new GZipStream(ms, System.IO.Compression.CompressionLevel.Optimal, true))
                gz.Write(raw, 0, raw.Length);
            return CodePrefix + Convert.ToBase64String(ms.ToArray());
        }
    }

    /// <summary>セーブコードを読み取る。改行や空白は無視する。失敗したら error に理由を入れる。</summary>
    public static bool TryParseCode(string code, out SaveData data, out string error)
    {
        data = null;
        error = null;
        if (string.IsNullOrWhiteSpace(code)) { error = "セーブコードを入力してください。"; return false; }

        var sb = new StringBuilder();
        foreach (char c in code) if (!char.IsWhiteSpace(c)) sb.Append(c);
        string s = sb.ToString();
        if (!s.StartsWith(CodePrefix)) { error = "セーブコードの形式が正しくありません。"; return false; }

        try
        {
            byte[] bytes = Convert.FromBase64String(s.Substring(CodePrefix.Length));
            using (var input = new MemoryStream(bytes))
            using (var gz = new GZipStream(input, CompressionMode.Decompress))
            using (var output = new MemoryStream())
            {
                gz.CopyTo(output);
                data = JsonUtility.FromJson<SaveData>(Encoding.UTF8.GetString(output.ToArray()));
            }
            if (data == null) { error = "セーブコードを読み取れませんでした。"; return false; }
            return true;
        }
        catch (Exception)
        {
            error = "セーブコードが壊れているか、途中で切れています。";
            data = null;
            return false;
        }
    }

    public static bool TryLoad(out SaveData data)
    {
        data = null;
        try
        {
            if (!File.Exists(FilePath)) return false;
            data = JsonUtility.FromJson<SaveData>(File.ReadAllText(FilePath));
            return data != null;
        }
        catch (Exception e)
        {
            Debug.LogError("ロードに失敗しました: " + e.Message);
            return false;
        }
    }

    public static void Delete()
    {
        try { if (File.Exists(FilePath)) File.Delete(FilePath); }
        catch (Exception e) { Debug.LogError(e.Message); }
    }
}

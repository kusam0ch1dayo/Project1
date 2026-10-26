using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace RemoteMenu
{
    [Serializable]
    public class HistoryEntry
    {
        public int seq;
        public long time;
        public string op;          // set / insert / delete / dup / trash / undo
        public string asset;
        public string assetLabel;
        public string label;
        public string oldValue;
        public string newValue;
        public string snapshot;    // 変更前のアセット全体(EditorJsonUtility)
        public bool undoable;
        public bool undone;
    }

    /// <summary>スマホからの変更履歴。Library/ に保存され、スクリプト再読み込みや Unity 再起動後も残る。</summary>
    [FilePath("Library/RemoteMenuHistory.asset", FilePathAttribute.Location.ProjectFolder)]
    public class RemoteHistory : ScriptableSingleton<RemoteHistory>
    {
        const int MaxEntries = 100;

        public int nextSeq = 1;
        public List<HistoryEntry> entries = new List<HistoryEntry>();

        public HistoryEntry Add(HistoryEntry e)
        {
            e.seq = nextSeq++;
            e.time = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            entries.Add(e);
            while (entries.Count > MaxEntries) entries.RemoveAt(0);
            Save(true);
            return e;
        }

        public HistoryEntry Find(int seq) => entries.Find(e => e.seq == seq);

        public void Persist() => Save(true);
    }
}

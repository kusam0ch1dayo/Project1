using System;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace RemoteMenu
{
    /// <summary>
    /// Console のログをスマホへ流す。スマホでログ画面を開いている間(logsub を受けてから 90 秒)だけ送る。
    /// </summary>
    public static class RemoteLog
    {
        struct Entry
        {
            public string type, msg, stack;
            public long time;
        }

        const int MaxEntries = 200;
        static readonly object gate = new object();
        static readonly List<Entry> recent = new List<Entry>();
        static readonly List<Entry> pending = new List<Entry>();
        static double subscribedUntil;
        static double lastFlush;
        static bool initialized;

        public static void Init()
        {
            if (initialized) return;
            initialized = true;
            Application.logMessageReceivedThreaded += OnLog;
        }

        static string Trunc(string s, int n) => s == null ? "" : s.Length > n ? s.Substring(0, n) + "…" : s;

        static void OnLog(string condition, string stack, LogType type)
        {
            if (type == LogType.Log && condition.StartsWith("[RemoteMenu]")) return;
            var isError = type == LogType.Error || type == LogType.Exception || type == LogType.Assert;
            var e = new Entry
            {
                type = isError ? "Error" : type.ToString(),
                msg = Trunc(condition, 2000),
                stack = isError ? Trunc(stack, 3000) : "",
                time = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            };
            lock (gate)
            {
                recent.Add(e);
                if (recent.Count > MaxEntries) recent.RemoveAt(0);
                pending.Add(e);
                if (pending.Count > MaxEntries) pending.RemoveAt(0);
            }
        }

        public static void Subscribe(bool backlog)
        {
            subscribedUntil = EditorApplication.timeSinceStartup + 90;
            if (!backlog) return;
            lock (gate)
            {
                pending.Clear();
                pending.AddRange(recent);
            }
        }

        /// <summary>メインスレッドから毎フレーム呼ぶ。0.5 秒ごとにまとめて送る。</summary>
        public static void Flush(Action<string> send)
        {
            var now = EditorApplication.timeSinceStartup;
            if (now < lastFlush + 0.5) return;
            lastFlush = now;

            List<Entry> batch;
            lock (gate)
            {
                if (pending.Count == 0) return;
                if (now > subscribedUntil) { pending.Clear(); return; }
                batch = new List<Entry>(pending);
                pending.Clear();
            }

            var sb = new StringBuilder("{\"t\":\"log\",\"items\":[");
            for (var i = 0; i < batch.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append("{\"type\":"); RemoteDataHandler.Q(sb, batch[i].type);
                sb.Append(",\"msg\":"); RemoteDataHandler.Q(sb, batch[i].msg);
                sb.Append(",\"stack\":"); RemoteDataHandler.Q(sb, batch[i].stack);
                sb.Append(",\"time\":").Append(batch[i].time).Append('}');
            }
            send(sb.Append("]}").ToString());
        }
    }
}

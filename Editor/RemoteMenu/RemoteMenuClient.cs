using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.WebSockets;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace RemoteMenu
{
    [Serializable]
    public class Msg
    {
        public string t;
        public string id;
        public string path;
        public bool ok;
        public string error;
        public string[] items;
        public string[] logs;
    }

    [Serializable]
    public class RemoteMenuSettings
    {
        public string url = "";
        public string token = "";
        public bool enabled;
        public List<string> allowed = new List<string>();
    }

    /// <summary>
    /// 中継サーバーへ WebSocket で常時接続し、許可済みの [MenuItem] をリモートから実行する。
    /// 設定は UserSettings/RemoteMenu.json（Git 管理外）に保存される。
    /// </summary>
    [InitializeOnLoad]
    public static class RemoteMenuClient
    {
        const string SettingsPath = "UserSettings/RemoteMenu.json";
        const string OwnMenuPrefix = "Tools/Remote Menu/";
        static readonly Regex ShortcutSuffix = new Regex(@"\s[%#&_]\S*$");

        public static RemoteMenuSettings Settings { get; private set; }
        public static volatile string Status = "off";

        static readonly ConcurrentQueue<string> inbox = new ConcurrentQueue<string>();
        static readonly SemaphoreSlim sendLock = new SemaphoreSlim(1, 1);
        static CancellationTokenSource cts;
        static ClientWebSocket socket;
        static volatile bool menusDirty;

        static RemoteMenuClient()
        {
            Settings = Load();
            EditorApplication.update += Pump;
            AssemblyReloadEvents.beforeAssemblyReload += Stop;
            EditorApplication.quitting += Stop;
            if (Settings.enabled) Start();
        }

        // ---- 設定 ----

        static RemoteMenuSettings Load()
        {
            try
            {
                if (File.Exists(SettingsPath))
                    return JsonUtility.FromJson<RemoteMenuSettings>(File.ReadAllText(SettingsPath)) ?? new RemoteMenuSettings();
            }
            catch (Exception e) { Debug.LogWarning("[RemoteMenu] 設定の読み込みに失敗: " + e.Message); }
            return new RemoteMenuSettings();
        }

        public static void SaveSettings()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath));
            File.WriteAllText(SettingsPath, JsonUtility.ToJson(Settings, true));
            menusDirty = true;
        }

        /// <summary>URL・トークン・有効/無効の変更を反映して再接続する。</summary>
        public static void Apply()
        {
            SaveSettings();
            Stop();
            if (Settings.enabled) Start();
        }

        // ---- メニュー列挙 ----

        /// <summary>プロジェクト内の [MenuItem]（Validate 関数と CONTEXT/ を除く）のパス一覧。</summary>
        public static List<string> Discover()
        {
            var menuField = typeof(MenuItem).GetField("menuItem", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            var validateField = typeof(MenuItem).GetField("validate", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            var result = new SortedSet<string>(StringComparer.Ordinal);
            if (menuField == null) return result.ToList();

            foreach (var method in TypeCache.GetMethodsWithAttribute<MenuItem>())
            {
                foreach (MenuItem attr in method.GetCustomAttributes(typeof(MenuItem), false))
                {
                    if (validateField != null && (bool)validateField.GetValue(attr)) continue;
                    var path = ShortcutSuffix.Replace((string)menuField.GetValue(attr) ?? "", "");
                    if (path.Length == 0 || path.StartsWith("CONTEXT/") || path.StartsWith(OwnMenuPrefix)) continue;
                    result.Add(path);
                }
            }
            return result.ToList();
        }

        static List<string> AllowedMenus()
        {
            var allowed = new HashSet<string>(Settings.allowed);
            return Discover().Where(allowed.Contains).ToList();
        }

        // ---- 接続 ----

        static void Start()
        {
            Stop();
            if (string.IsNullOrEmpty(Settings.url) || string.IsNullOrEmpty(Settings.token))
            {
                Status = "URL とトークンを設定してください";
                return;
            }
            cts = new CancellationTokenSource();
            var ct = cts.Token;
            var uri = new Uri(Settings.url.TrimEnd('/') + "/ws?role=unity&token=" + Uri.EscapeDataString(Settings.token));
            Status = "connecting";
            Task.Run(() => Loop(uri, ct));
        }

        static void Stop()
        {
            try { cts?.Cancel(); } catch { }
            try { socket?.Abort(); } catch { }
            cts = null;
            socket = null;
            Status = "off";
        }

        static async Task Loop(Uri uri, CancellationToken ct)
        {
            var delay = 1000;
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    using (var s = new ClientWebSocket())
                    {
                        await s.ConnectAsync(uri, ct);
                        socket = s;
                        delay = 1000;
                        Status = "connected";
                        menusDirty = true;
                        _ = PingLoop(s, ct);
                        await Receive(s, ct);
                    }
                }
                catch (OperationCanceledException) { break; }
                catch (Exception e) { Status = "error: " + e.Message; }

                socket = null;
                if (ct.IsCancellationRequested) break;
                try { await Task.Delay(delay, ct); } catch { break; }
                delay = Math.Min(delay * 2, 30000);
            }
        }

        static async Task Receive(ClientWebSocket s, CancellationToken ct)
        {
            var buf = new byte[16 * 1024];
            var sb = new StringBuilder();
            while (s.State == WebSocketState.Open && !ct.IsCancellationRequested)
            {
                var r = await s.ReceiveAsync(new ArraySegment<byte>(buf), ct);
                if (r.MessageType == WebSocketMessageType.Close) return;
                sb.Append(Encoding.UTF8.GetString(buf, 0, r.Count));
                if (!r.EndOfMessage) continue;
                var text = sb.ToString();
                sb.Clear();
                if (text.StartsWith("{")) inbox.Enqueue(text);
            }
        }

        static async Task PingLoop(ClientWebSocket s, CancellationToken ct)
        {
            try
            {
                while (s.State == WebSocketState.Open)
                {
                    await Task.Delay(30000, ct);
                    await SendAsync(s, "ping", ct);
                }
            }
            catch { }
        }

        static async Task SendAsync(ClientWebSocket s, string text, CancellationToken ct)
        {
            var bytes = Encoding.UTF8.GetBytes(text);
            await sendLock.WaitAsync(ct);
            try
            {
                if (s.State == WebSocketState.Open)
                    await s.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, ct);
            }
            finally { sendLock.Release(); }
        }

        static void Send(Msg m)
        {
            var s = socket;
            var c = cts;
            if (s == null || c == null) return;
            SendAsync(s, JsonUtility.ToJson(m), c.Token).ContinueWith(t => { var _ = t.Exception; });
        }

        // ---- メインスレッド処理 ----

        static void Pump()
        {
            if (menusDirty && socket != null)
            {
                menusDirty = false;
                Send(new Msg { t = "menus", items = AllowedMenus().ToArray() });
            }

            while (inbox.TryDequeue(out var text))
            {
                Msg m;
                try { m = JsonUtility.FromJson<Msg>(text); }
                catch { continue; }
                if (m != null && m.t == "run") Run(m);
            }
        }

        static void Run(Msg req)
        {
            var logs = new List<string>();
            void OnLog(string condition, string stack, LogType type)
            {
                if (logs.Count < 50) logs.Add("[" + type + "] " + (condition.Length > 300 ? condition.Substring(0, 300) : condition));
            }

            var ok = false;
            string error = null;
            Application.logMessageReceived += OnLog;
            try
            {
                if (string.IsNullOrEmpty(req.path) || !AllowedMenus().Contains(req.path))
                    error = "許可されていないメニューです";
                else
                {
                    ok = EditorApplication.ExecuteMenuItem(req.path);
                    if (!ok) error = "メニューが見つからないか、現在は実行できません";
                }
            }
            catch (Exception e) { error = e.Message; }
            finally { Application.logMessageReceived -= OnLog; }

            Debug.Log("[RemoteMenu] " + req.path + " -> " + (ok ? "OK" : "NG: " + error));
            Send(new Msg { t = "result", id = req.id, path = req.path, ok = ok, error = error ?? "", logs = logs.ToArray() });
        }
    }
}

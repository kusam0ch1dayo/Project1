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
        public string asset;
        public string prop;
        public string op;
        public string value;
        public int index;
        public bool backlog;
    }

    [Serializable]
    public class RemoteMenuSettings
    {
        public string url = "";
        public string token = "";
        public bool enabled;
        public List<string> allowed = new List<string>();
        public List<string> allowedTypes = new List<string>();
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
            // アセットインポート用の裏プロセス(Unity Editor が複数見えるもの)でも [InitializeOnLoad] は走る。
            // そこから接続するとメイン Editor の接続を追い出してしまうので、メインの Editor だけで動かす。
            if (AssetDatabase.IsAssetImportWorkerProcess() || Application.isBatchMode ||
                UnityEditor.MPE.ProcessService.level != UnityEditor.MPE.ProcessLevel.Main)
                return;
            RemoteLog.Init();
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
                {
                    var loaded = JsonUtility.FromJson<RemoteMenuSettings>(File.ReadAllText(SettingsPath)) ?? new RemoteMenuSettings();
                    if (loaded.allowed == null) loaded.allowed = new List<string>();
                    if (loaded.allowedTypes == null) loaded.allowedTypes = new List<string>();
                    return loaded;
                }
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
                        Debug.Log("[RemoteMenu] サーバーに接続しました");
                        _ = PingLoop(s, ct);
                        await Receive(s, ct);
                        if (s.CloseStatusDescription == "replaced")
                        {
                            // 別の Unity(または再読み込み前に残った古い接続)と追い出し合わないよう、ここで止まる
                            Debug.LogWarning("[RemoteMenu] 別の接続に置き換えられたため、この接続を停止しました。Unity が2つ起動していないか確認してください。");
                            socket = null;
                            Status = "停止: 別の接続に置き換えられました(「保存して接続を反映」で再開)";
                            return;
                        }
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

        static void Send(Msg m) => SendRaw(JsonUtility.ToJson(m));

        static void SendRaw(string json)
        {
            var s = socket;
            var c = cts;
            if (s == null || c == null)
            {
                Debug.LogWarning("[RemoteMenu] 送信できません(未接続): " + (json.Length > 80 ? json.Substring(0, 80) : json));
                return;
            }
            var head = json.Length > 60 ? json.Substring(0, 60) : json;
            SendAsync(s, json, c.Token).ContinueWith(t =>
            {
                if (t.IsFaulted) Debug.LogWarning("[RemoteMenu] 送信に失敗: " + t.Exception?.GetBaseException().Message + " / " + head);
            });
        }

        // ---- メインスレッド処理 ----

        static void Pump()
        {
            if (menusDirty && socket != null)
            {
                menusDirty = false;
                Send(new Msg { t = "menus", items = AllowedMenus().ToArray() });
            }

            if (socket != null) RemoteLog.Flush(SendRaw);

            while (inbox.TryDequeue(out var text))
            {
                Msg m;
                try { m = JsonUtility.FromJson<Msg>(text); }
                catch (Exception e)
                {
                    Debug.LogWarning("[RemoteMenu] 受信メッセージを解釈できません: " + e.Message + " / " + (text.Length > 200 ? text.Substring(0, 200) : text));
                    continue;
                }
                if (m == null) continue;
                if (m.t == "run") Run(m);
                else if (m.t == "logsub") RemoteLog.Subscribe(m.backlog);
                else
                {
                    var reply = RemoteDataHandler.Handle(m);
                    if (reply != null) SendRaw(reply);
                }
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

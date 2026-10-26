using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace RemoteMenu
{
    public class RemoteMenuWindow : EditorWindow
    {
        List<string> menus = new List<string>();
        List<string> dataTypes = new List<string>();
        int tab;
        string filter = "";
        Vector2 scroll;

        [MenuItem("Tools/Remote Menu/Settings")]
        static void Open() => GetWindow<RemoteMenuWindow>("Remote Menu");

        void OnEnable()
        {
            menus = RemoteMenuClient.Discover();
            dataTypes = RemoteDataHandler.DiscoverTypes();
        }

        void OnInspectorUpdate() => Repaint();

        void OnGUI()
        {
            var s = RemoteMenuClient.Settings;

            s.url = EditorGUILayout.TextField("Server URL (wss://...)", s.url);
            s.token = EditorGUILayout.PasswordField("Token", s.token);
            s.enabled = EditorGUILayout.Toggle("有効", s.enabled);
            if (GUILayout.Button("保存して接続を反映")) RemoteMenuClient.Apply();
            EditorGUILayout.HelpBox("状態: " + RemoteMenuClient.Status, MessageType.None);

            EditorGUILayout.Space();
            tab = GUILayout.Toolbar(tab, new[] { "メニュー実行", "データ編集" });
            EditorGUILayout.BeginHorizontal();
            filter = EditorGUILayout.TextField("絞り込み", filter);
            if (GUILayout.Button("再読込", GUILayout.Width(60)))
            {
                menus = RemoteMenuClient.Discover();
                dataTypes = RemoteDataHandler.DiscoverTypes();
            }
            EditorGUILayout.EndHorizontal();

            if (tab == 1) EditorGUILayout.HelpBox("チェックした型の ScriptableObject アセットを、スマホから編集できます。", MessageType.Info);

            scroll = EditorGUILayout.BeginScrollView(scroll);
            DrawChecklist(tab == 0 ? menus : dataTypes, tab == 0 ? s.allowed : s.allowedTypes);
            EditorGUILayout.EndScrollView();
        }

        void DrawChecklist(List<string> all, List<string> allowed)
        {
            foreach (var item in all)
            {
                if (filter.Length > 0 && item.IndexOf(filter, System.StringComparison.OrdinalIgnoreCase) < 0) continue;
                var was = allowed.Contains(item);
                var now = EditorGUILayout.ToggleLeft(item, was);
                if (now == was) continue;
                if (now) allowed.Add(item); else allowed.Remove(item);
                RemoteMenuClient.SaveSettings();
            }
        }
    }
}

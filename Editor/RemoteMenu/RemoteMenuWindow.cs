using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace RemoteMenu
{
    public class RemoteMenuWindow : EditorWindow
    {
        List<string> menus = new List<string>();
        string filter = "";
        Vector2 scroll;

        [MenuItem("Tools/Remote Menu/Settings")]
        static void Open() => GetWindow<RemoteMenuWindow>("Remote Menu");

        void OnEnable() => menus = RemoteMenuClient.Discover();

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
            EditorGUILayout.LabelField("リモート実行を許可するメニュー", EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal();
            filter = EditorGUILayout.TextField("絞り込み", filter);
            if (GUILayout.Button("再読込", GUILayout.Width(60))) menus = RemoteMenuClient.Discover();
            EditorGUILayout.EndHorizontal();

            scroll = EditorGUILayout.BeginScrollView(scroll);
            foreach (var path in menus)
            {
                if (filter.Length > 0 && path.IndexOf(filter, System.StringComparison.OrdinalIgnoreCase) < 0) continue;
                var was = s.allowed.Contains(path);
                var now = EditorGUILayout.ToggleLeft(path, was);
                if (now == was) continue;
                if (now) s.allowed.Add(path); else s.allowed.Remove(path);
                RemoteMenuClient.SaveSettings();
            }
            EditorGUILayout.EndScrollView();
        }
    }
}

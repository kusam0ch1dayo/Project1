using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace RemoteMenu
{
    /// <summary>
    /// ScriptableObject アセットを SerializedObject 経由でスマホから閲覧・編集するための処理。
    /// 編集できるのは、設定画面で許可した型のアセット(Assets/ 配下)だけ。
    /// </summary>
    public static class RemoteDataHandler
    {
        const int MaxListItems = 300;
        const int MaxCandidates = 400;
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        class Ctx
        {
            public readonly Dictionary<string, List<KeyValuePair<string, string>>> refs = new Dictionary<string, List<KeyValuePair<string, string>>>();
        }

        public static string Handle(Msg m)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                string reply = null;
                switch (m.t)
                {
                    case "list": reply = ListAssets(m); break;
                    case "get": reply = Get(m); break;
                    case "set": reply = Set(m); break;
                    case "dup": reply = Dup(m); break;
                    case "trash": reply = Trash(m); break;
                    case "history": reply = History(m); break;
                    case "undo": reply = UndoEntry(m); break;
                }
                if (sw.ElapsedMilliseconds > 1000) Debug.Log("[RemoteMenu] " + m.t + " に " + sw.ElapsedMilliseconds + " ms かかりました");
                return reply;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[RemoteMenu] " + m.t + " 失敗: " + e);
                return Ack(m, false, e.Message, null);
            }
        }

        // ---- JSON 出力の補助 ----

        internal static void Q(StringBuilder sb, string s)
        {
            sb.Append('"');
            foreach (var c in s ?? "")
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }

        static string Num(double d) => double.IsNaN(d) || double.IsInfinity(d) ? "0" : d.ToString("R", Inv);

        static string Ack(Msg m, bool ok, string error, string newAsset)
        {
            var sb = new StringBuilder("{\"t\":\"ack\",\"id\":");
            Q(sb, m.id);
            sb.Append(",\"op\":"); Q(sb, m.t);
            sb.Append(",\"ok\":").Append(ok ? "true" : "false");
            sb.Append(",\"error\":"); Q(sb, error ?? "");
            sb.Append(",\"newAsset\":"); Q(sb, newAsset ?? "");
            return sb.Append('}').ToString();
        }

        // ---- 許可チェック ----

        static ScriptableObject LoadAllowed(string path, out string error)
        {
            error = null;
            if (string.IsNullOrEmpty(path) || !path.StartsWith("Assets/") || path.Contains(".."))
            {
                error = "不正なパスです";
                return null;
            }
            var o = AssetDatabase.LoadMainAssetAtPath(path) as ScriptableObject;
            if (o == null) { error = "アセットが見つかりません"; return null; }
            if (!RemoteMenuClient.Settings.allowedTypes.Contains(o.GetType().Name)) { error = "この種類は許可されていません"; return null; }
            return o;
        }

        /// <summary>Assets/ 配下にある ScriptableObject アセットの型名一覧(設定画面用)。</summary>
        public static List<string> DiscoverTypes()
        {
            var set = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var guid in AssetDatabase.FindAssets("t:ScriptableObject", new[] { "Assets" }))
            {
                var t = AssetDatabase.GetMainAssetTypeAtPath(AssetDatabase.GUIDToAssetPath(guid));
                if (t != null && typeof(ScriptableObject).IsAssignableFrom(t)) set.Add(t.Name);
            }
            return set.ToList();
        }

        // ---- 一覧 ----

        static string LabelOf(UnityEngine.Object o, out int category, out int order, out bool hasOrder)
        {
            category = 0; order = 0; hasOrder = false;
            var label = o.name;
            if (!(o is ScriptableObject)) return label;
            using (var so = new SerializedObject(o))
            {
                var dn = so.FindProperty("displayName");
                if (dn != null && dn.propertyType == SerializedPropertyType.String && !string.IsNullOrEmpty(dn.stringValue)) label = dn.stringValue;
                var ord = so.FindProperty("order");
                if (ord != null && ord.propertyType == SerializedPropertyType.Integer)
                {
                    hasOrder = true;
                    order = ord.intValue;
                    var cat = so.FindProperty("category");
                    string catName = null;
                    if (cat != null && cat.propertyType == SerializedPropertyType.Enum && cat.enumValueIndex >= 0 && cat.enumValueIndex < cat.enumDisplayNames.Length)
                    {
                        category = cat.enumValueIndex;
                        catName = cat.enumDisplayNames[cat.enumValueIndex];
                    }
                    label = "[" + (catName != null ? catName + " " : "") + order + "] " + label;
                }
            }
            return label;
        }

        const int MaxColumns = 40;

        /// <summary>一覧での絞り込み・並べ替えに使う、トップレベルの単純な項目(数値・選択肢・ON/OFF)。</summary>
        static List<(string name, string label, string kind)> SimpleColumns(SerializedObject so)
        {
            var cols = new List<(string, string, string)>();
            var it = so.GetIterator();
            var enter = true;
            while (it.NextVisible(enter) && cols.Count < MaxColumns)
            {
                enter = false;
                switch (it.propertyType)
                {
                    case SerializedPropertyType.Integer:
                    case SerializedPropertyType.Float:
                        cols.Add((it.name, it.displayName, "num"));
                        break;
                    case SerializedPropertyType.Boolean:
                        cols.Add((it.name, it.displayName, "bool"));
                        break;
                    case SerializedPropertyType.Enum:
                        ResolveField(it, out var t);
                        if (!IsFlags(t)) cols.Add((it.name, it.displayName, "enum"));
                        break;
                }
            }
            return cols;
        }

        static void WriteColumnValue(StringBuilder sb, SerializedProperty p)
        {
            if (p == null) { sb.Append("null"); return; }
            switch (p.propertyType)
            {
                case SerializedPropertyType.Integer: sb.Append(p.longValue.ToString(Inv)); break;
                case SerializedPropertyType.Float: sb.Append(Num(p.floatValue)); break;
                case SerializedPropertyType.Boolean: sb.Append(p.boolValue ? "true" : "false"); break;
                case SerializedPropertyType.Enum:
                    var idx = p.enumValueIndex;
                    Q(sb, idx >= 0 && idx < p.enumDisplayNames.Length ? p.enumDisplayNames[idx] : "");
                    break;
                default: sb.Append("null"); break;
            }
        }

        static string ListAssets(Msg m)
        {
            var sb = new StringBuilder("{\"t\":\"list\",\"id\":");
            Q(sb, m.id);
            sb.Append(",\"types\":[");
            var firstType = true;
            foreach (var type in RemoteMenuClient.Settings.allowedTypes.OrderBy(x => x, StringComparer.Ordinal))
            {
                var items = new List<(string path, string label, int cat, int order, string values)>();
                List<(string name, string label, string kind)> cols = null;
                foreach (var guid in AssetDatabase.FindAssets("t:" + type, new[] { "Assets" }))
                {
                    var path = AssetDatabase.GUIDToAssetPath(guid);
                    var o = AssetDatabase.LoadMainAssetAtPath(path) as ScriptableObject;
                    if (o == null || o.GetType().Name != type) continue;
                    var label = LabelOf(o, out var cat, out var order, out _);
                    var vals = new StringBuilder();
                    using (var so = new SerializedObject(o))
                    {
                        if (cols == null) cols = SimpleColumns(so);
                        for (var i = 0; i < cols.Count; i++)
                        {
                            if (i > 0) vals.Append(',');
                            WriteColumnValue(vals, so.FindProperty(cols[i].name));
                        }
                    }
                    items.Add((path, label, cat, order, vals.ToString()));
                }
                items = items.OrderBy(i => i.cat).ThenBy(i => i.order).ThenBy(i => i.label, StringComparer.Ordinal).ToList();

                if (!firstType) sb.Append(',');
                firstType = false;
                sb.Append("{\"type\":"); Q(sb, type);
                sb.Append(",\"cols\":[");
                for (var i = 0; cols != null && i < cols.Count; i++)
                {
                    if (i > 0) sb.Append(',');
                    sb.Append("{\"name\":"); Q(sb, cols[i].name);
                    sb.Append(",\"label\":"); Q(sb, cols[i].label);
                    sb.Append(",\"kind\":"); Q(sb, cols[i].kind);
                    sb.Append('}');
                }
                sb.Append("],\"assets\":[");
                for (var i = 0; i < items.Count; i++)
                {
                    if (i > 0) sb.Append(',');
                    sb.Append("{\"p\":"); Q(sb, items[i].path);
                    sb.Append(",\"n\":"); Q(sb, items[i].label);
                    sb.Append(",\"v\":[").Append(items[i].values).Append("]}");
                }
                sb.Append("]}");
            }
            return sb.Append("]}").ToString();
        }

        // ---- 取得(プロパティ木の出力) ----

        static string Get(Msg m)
        {
            var o = LoadAllowed(m.asset, out var err);
            if (o == null) return Ack(m, false, err, null);

            var ctx = new Ctx();
            var sb = new StringBuilder("{\"t\":\"asset\",\"id\":");
            Q(sb, m.id);
            sb.Append(",\"asset\":"); Q(sb, m.asset);
            sb.Append(",\"name\":"); Q(sb, o.name);
            sb.Append(",\"type\":"); Q(sb, o.GetType().Name);
            sb.Append(",\"tree\":[");
            using (var so = new SerializedObject(o))
            {
                var it = so.GetIterator();
                var enter = true;
                var first = true;
                while (it.NextVisible(enter))
                {
                    enter = false;
                    if (it.name == "m_Script") continue;
                    if (!first) sb.Append(',');
                    first = false;
                    WriteProp(sb, it, ctx);
                }
            }
            sb.Append("],\"refs\":{");
            var firstRef = true;
            foreach (var kv in ctx.refs)
            {
                if (!firstRef) sb.Append(',');
                firstRef = false;
                Q(sb, kv.Key);
                sb.Append(":[");
                for (var i = 0; i < kv.Value.Count; i++)
                {
                    if (i > 0) sb.Append(',');
                    sb.Append("{\"p\":"); Q(sb, kv.Value[i].Key);
                    sb.Append(",\"n\":"); Q(sb, kv.Value[i].Value);
                    sb.Append('}');
                }
                sb.Append(']');
            }
            return sb.Append("}}").ToString();
        }

        static FieldInfo FindField(Type type, string name)
        {
            for (var t = type; t != null; t = t.BaseType)
            {
                var f = t.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                if (f != null) return f;
            }
            return null;
        }

        static Type ElementType(Type t)
        {
            if (t == null) return null;
            if (t.IsArray) return t.GetElementType();
            return t.IsGenericType ? t.GetGenericArguments()[0] : null;
        }

        /// <summary>プロパティパスから、C# 側のフィールドと型を調べる(配列要素のときは型だけ)。</summary>
        static FieldInfo ResolveField(SerializedProperty p, out Type type)
        {
            type = p.serializedObject.targetObject.GetType();
            FieldInfo fi = null;
            var parts = p.propertyPath.Split('.');
            for (var i = 0; i < parts.Length && type != null; i++)
            {
                var part = parts[i];
                if (part == "Array") continue;
                if (part.StartsWith("data["))
                {
                    type = ElementType(type);
                    fi = null;
                    continue;
                }
                fi = FindField(type, part);
                type = fi?.FieldType;
            }
            return fi;
        }

        static bool IsFlags(Type t) => t != null && t.IsEnum && t.IsDefined(typeof(FlagsAttribute), false);

        static void NeedCandidates(Ctx ctx, Type t)
        {
            if (t == null || t == typeof(UnityEngine.Object) || ctx.refs.ContainsKey(t.Name)) return;
            var list = new List<KeyValuePair<string, string>>();
            var isSo = typeof(ScriptableObject).IsAssignableFrom(t);
            foreach (var guid in AssetDatabase.FindAssets("t:" + t.Name, new[] { "Assets" }).Take(MaxCandidates))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var label = Path.GetFileNameWithoutExtension(path);
                if (isSo)
                {
                    var o = AssetDatabase.LoadMainAssetAtPath(path);
                    if (o != null) label = LabelOf(o, out _, out _, out _);
                }
                list.Add(new KeyValuePair<string, string>(path, label));
            }
            list.Sort((a, b) => string.Compare(a.Value, b.Value, StringComparison.Ordinal));
            ctx.refs[t.Name] = list;
        }

        static void WriteProp(StringBuilder sb, SerializedProperty p, Ctx ctx)
        {
            var fi = ResolveField(p, out var ftype);
            sb.Append("{\"path\":"); Q(sb, p.propertyPath);
            sb.Append(",\"label\":"); Q(sb, p.displayName);
            var tip = fi?.GetCustomAttribute<TooltipAttribute>()?.tooltip;
            if (!string.IsNullOrEmpty(tip)) { sb.Append(",\"tip\":"); Q(sb, tip); }

            switch (p.propertyType)
            {
                case SerializedPropertyType.Boolean:
                    sb.Append(",\"k\":\"bool\",\"v\":").Append(p.boolValue ? "true" : "false");
                    break;
                case SerializedPropertyType.Integer:
                case SerializedPropertyType.Character:
                case SerializedPropertyType.LayerMask:
                    sb.Append(",\"k\":\"int\",\"v\":").Append(p.propertyType == SerializedPropertyType.Integer ? p.longValue.ToString(Inv) : p.intValue.ToString(Inv));
                    break;
                case SerializedPropertyType.Float:
                    sb.Append(",\"k\":\"float\",\"v\":").Append(Num(p.numericType == SerializedPropertyNumericType.Double ? p.doubleValue : p.floatValue));
                    break;
                case SerializedPropertyType.String:
                    sb.Append(",\"k\":\"string\",\"v\":"); Q(sb, p.stringValue);
                    break;
                case SerializedPropertyType.Enum:
                    if (IsFlags(ftype))
                    {
                        sb.Append(",\"k\":\"flags\",\"v\":").Append(p.enumValueFlag.ToString(Inv)).Append(",\"opts\":[");
                        var first = true;
                        foreach (var name in Enum.GetNames(ftype))
                        {
                            var val = Convert.ToInt64(Enum.Parse(ftype, name));
                            if (val == 0 || (val & (val - 1)) != 0) continue;
                            if (!first) sb.Append(',');
                            first = false;
                            sb.Append("{\"n\":"); Q(sb, ObjectNames.NicifyVariableName(name));
                            sb.Append(",\"b\":").Append(val.ToString(Inv)).Append('}');
                        }
                        sb.Append(']');
                    }
                    else
                    {
                        sb.Append(",\"k\":\"enum\",\"v\":").Append(p.enumValueIndex.ToString(Inv)).Append(",\"opts\":[");
                        var names = p.enumDisplayNames;
                        for (var i = 0; i < names.Length; i++)
                        {
                            if (i > 0) sb.Append(',');
                            Q(sb, names[i]);
                        }
                        sb.Append(']');
                    }
                    break;
                case SerializedPropertyType.ObjectReference:
                {
                    var obj = p.objectReferenceValue;
                    var path = obj != null ? AssetDatabase.GetAssetPath(obj) : "";
                    if (obj != null && string.IsNullOrEmpty(path))
                    {
                        sb.Append(",\"k\":\"unsupported\",\"why\":\"シーン内のオブジェクト\"");
                        break;
                    }
                    NeedCandidates(ctx, ftype);
                    sb.Append(",\"k\":\"obj\",\"type\":"); Q(sb, ftype != null ? ftype.Name : "Object");
                    sb.Append(",\"v\":"); Q(sb, path);
                    sb.Append(",\"vn\":"); Q(sb, obj != null ? (obj is ScriptableObject ? LabelOf(obj, out _, out _, out _) : obj.name) : "");
                    break;
                }
                case SerializedPropertyType.Vector2:
                    sb.Append(",\"k\":\"vec\",\"int\":false,\"v\":[").Append(Num(p.vector2Value.x)).Append(',').Append(Num(p.vector2Value.y)).Append(']');
                    break;
                case SerializedPropertyType.Vector3:
                    sb.Append(",\"k\":\"vec\",\"int\":false,\"v\":[").Append(Num(p.vector3Value.x)).Append(',').Append(Num(p.vector3Value.y)).Append(',').Append(Num(p.vector3Value.z)).Append(']');
                    break;
                case SerializedPropertyType.Vector4:
                    sb.Append(",\"k\":\"vec\",\"int\":false,\"v\":[").Append(Num(p.vector4Value.x)).Append(',').Append(Num(p.vector4Value.y)).Append(',').Append(Num(p.vector4Value.z)).Append(',').Append(Num(p.vector4Value.w)).Append(']');
                    break;
                case SerializedPropertyType.Vector2Int:
                    sb.Append(",\"k\":\"vec\",\"int\":true,\"v\":[").Append(p.vector2IntValue.x).Append(',').Append(p.vector2IntValue.y).Append(']');
                    break;
                case SerializedPropertyType.Vector3Int:
                    sb.Append(",\"k\":\"vec\",\"int\":true,\"v\":[").Append(p.vector3IntValue.x).Append(',').Append(p.vector3IntValue.y).Append(',').Append(p.vector3IntValue.z).Append(']');
                    break;
                case SerializedPropertyType.Color:
                    sb.Append(",\"k\":\"color\",\"v\":"); Q(sb, "#" + ColorUtility.ToHtmlStringRGBA(p.colorValue));
                    break;
                case SerializedPropertyType.Generic:
                    if (p.isArray)
                    {
                        var size = p.arraySize;
                        sb.Append(",\"k\":\"list\",\"size\":").Append(size).Append(",\"c\":[");
                        for (var i = 0; i < Math.Min(size, MaxListItems); i++)
                        {
                            if (i > 0) sb.Append(',');
                            WriteProp(sb, p.GetArrayElementAtIndex(i), ctx);
                        }
                        sb.Append(']');
                    }
                    else
                    {
                        sb.Append(",\"k\":\"group\",\"c\":[");
                        var child = p.Copy();
                        var end = p.GetEndProperty();
                        var enter = true;
                        var first = true;
                        while (child.NextVisible(enter) && !SerializedProperty.EqualContents(child, end))
                        {
                            enter = false;
                            if (!first) sb.Append(',');
                            first = false;
                            WriteProp(sb, child, ctx);
                        }
                        sb.Append(']');
                    }
                    break;
                default:
                    sb.Append(",\"k\":\"unsupported\",\"why\":"); Q(sb, p.propertyType.ToString());
                    break;
            }
            sb.Append('}');
        }

        // ---- 編集 ----

        static string Set(Msg m)
        {
            var o = LoadAllowed(m.asset, out var err);
            if (o == null) return Ack(m, false, err, null);

            var snapshot = EditorJsonUtility.ToJson(o);
            string oldValue, newValue;
            using (var so = new SerializedObject(o))
            {
                var p = so.FindProperty(m.prop);
                if (p == null) return Ack(m, false, "項目が見つかりません: " + m.prop, null);
                oldValue = Display(p);

                switch (m.op)
                {
                    case "insert":
                        if (!p.isArray) return Ack(m, false, "リストではありません", null);
                        p.InsertArrayElementAtIndex(Mathf.Clamp(m.index, 0, p.arraySize));
                        break;
                    case "delete":
                    {
                        if (!p.isArray || m.index < 0 || m.index >= p.arraySize) return Ack(m, false, "要素が見つかりません", null);
                        var before = p.arraySize;
                        p.DeleteArrayElementAtIndex(m.index);
                        if (p.arraySize == before) p.DeleteArrayElementAtIndex(m.index); // 参照型は1回目で null になるだけ
                        break;
                    }
                    default:
                        if (!SetValue(p, m.value ?? "", out err)) return Ack(m, false, err, null);
                        break;
                }
                so.ApplyModifiedProperties();
                newValue = Display(p);
            }
            EditorUtility.SetDirty(o);
            ScheduleSave();
            RemoteHistory.instance.Add(new HistoryEntry
            {
                op = m.op == "insert" || m.op == "delete" ? m.op : "set",
                asset = m.asset,
                assetLabel = LabelOf(o, out _, out _, out _),
                label = NicePath(m.prop) + (m.op == "insert" ? " に要素を追加" : m.op == "delete" ? " の #" + m.index + " を削除" : ""),
                oldValue = oldValue,
                newValue = newValue,
                snapshot = snapshot,
                undoable = true,
            });
            return Ack(m, true, null, null);
        }

        static bool saveScheduled;

        /// <summary>応答を先に返し、ディスクへの保存は少し後にまとめて行う(保存が重くても画面が待たされない)。</summary>
        static void ScheduleSave()
        {
            if (saveScheduled) return;
            saveScheduled = true;
            EditorApplication.delayCall += () =>
            {
                saveScheduled = false;
                AssetDatabase.SaveAssets();
            };
        }

        static bool SetValue(SerializedProperty p, string v, out string error)
        {
            error = null;
            ResolveField(p, out var ftype);
            try
            {
                switch (p.propertyType)
                {
                    case SerializedPropertyType.Boolean: p.boolValue = v == "true"; return true;
                    case SerializedPropertyType.Integer: p.longValue = long.Parse(v, Inv); return true;
                    case SerializedPropertyType.Character:
                    case SerializedPropertyType.LayerMask: p.intValue = int.Parse(v, Inv); return true;
                    case SerializedPropertyType.Float:
                        if (p.numericType == SerializedPropertyNumericType.Double) p.doubleValue = double.Parse(v, Inv);
                        else p.floatValue = float.Parse(v, Inv);
                        return true;
                    case SerializedPropertyType.String: p.stringValue = v; return true;
                    case SerializedPropertyType.Enum:
                        if (IsFlags(ftype)) { p.enumValueFlag = int.Parse(v, Inv); return true; }
                        var idx = int.Parse(v, Inv);
                        if (idx < 0 || idx >= p.enumNames.Length) { error = "範囲外の選択肢です"; return false; }
                        p.enumValueIndex = idx;
                        return true;
                    case SerializedPropertyType.ObjectReference:
                        if (v.Length == 0) { p.objectReferenceValue = null; return true; }
                        if (!v.StartsWith("Assets/") || v.Contains("..")) { error = "不正なパスです"; return false; }
                        var obj = AssetDatabase.LoadAssetAtPath(v, ftype ?? typeof(UnityEngine.Object));
                        if (obj == null) { error = "アセットが見つかりません"; return false; }
                        p.objectReferenceValue = obj;
                        return true;
                    case SerializedPropertyType.Vector2: { var a = Floats(v, 2); p.vector2Value = new Vector2(a[0], a[1]); return true; }
                    case SerializedPropertyType.Vector3: { var a = Floats(v, 3); p.vector3Value = new Vector3(a[0], a[1], a[2]); return true; }
                    case SerializedPropertyType.Vector4: { var a = Floats(v, 4); p.vector4Value = new Vector4(a[0], a[1], a[2], a[3]); return true; }
                    case SerializedPropertyType.Vector2Int: { var a = Floats(v, 2); p.vector2IntValue = new Vector2Int((int)a[0], (int)a[1]); return true; }
                    case SerializedPropertyType.Vector3Int: { var a = Floats(v, 3); p.vector3IntValue = new Vector3Int((int)a[0], (int)a[1], (int)a[2]); return true; }
                    case SerializedPropertyType.Color:
                        if (!ColorUtility.TryParseHtmlString(v, out var c)) { error = "色の形式が不正です"; return false; }
                        p.colorValue = c;
                        return true;
                }
            }
            catch (Exception e) { error = "値を変換できません: " + e.Message; return false; }
            error = "この項目はスマホから編集できません";
            return false;
        }

        static float[] Floats(string v, int n)
        {
            var parts = v.Split(',');
            if (parts.Length != n) throw new FormatException("要素数が違います");
            return parts.Select(s => float.Parse(s, Inv)).ToArray();
        }

        // ---- 複製・削除 ----

        static string Dup(Msg m)
        {
            var o = LoadAllowed(m.asset, out var err);
            if (o == null) return Ack(m, false, err, null);

            var dir = Path.GetDirectoryName(m.asset).Replace('\\', '/');
            var dst = AssetDatabase.GenerateUniqueAssetPath(dir + "/" + Path.GetFileNameWithoutExtension(m.asset) + "_copy.asset");
            if (!AssetDatabase.CopyAsset(m.asset, dst)) return Ack(m, false, "複製に失敗しました", null);

            var copy = AssetDatabase.LoadMainAssetAtPath(dst);
            using (var so = new SerializedObject(copy))
            {
                var id = so.FindProperty("id");
                if (id != null && id.propertyType == SerializedPropertyType.String) id.stringValue = Path.GetFileNameWithoutExtension(dst);
                var dn = so.FindProperty("displayName");
                if (dn != null && dn.propertyType == SerializedPropertyType.String) dn.stringValue += "（複製）";
                so.ApplyModifiedProperties();
            }
            EditorUtility.SetDirty(copy);
            AssetDatabase.SaveAssets();
            RemoteHistory.instance.Add(new HistoryEntry
            {
                op = "dup", asset = dst, assetLabel = LabelOf(copy, out _, out _, out _),
                label = "複製を作成", oldValue = "", newValue = Path.GetFileName(dst), undoable = true,
            });
            return Ack(m, true, null, dst);
        }

        static string Trash(Msg m)
        {
            var o = LoadAllowed(m.asset, out var err);
            if (o == null) return Ack(m, false, err, null);
            var label = LabelOf(o, out _, out _, out _);
            if (!AssetDatabase.MoveAssetToTrash(m.asset)) return Ack(m, false, "削除に失敗しました", null);
            RemoteHistory.instance.Add(new HistoryEntry
            {
                op = "trash", asset = m.asset, assetLabel = label,
                label = "削除(ゴミ箱へ)", oldValue = "", newValue = "", undoable = false,
            });
            return Ack(m, true, null, null);
        }

        // ---- 履歴・元に戻す ----

        static string NicePath(string path)
        {
            var parts = path.Replace(".Array.data[", "[").Split('.');
            return string.Join(" / ", parts.Select(p =>
            {
                var i = p.IndexOf('[');
                return i < 0 ? ObjectNames.NicifyVariableName(p) : ObjectNames.NicifyVariableName(p.Substring(0, i)) + p.Substring(i);
            }));
        }

        /// <summary>履歴に表示するための、値の短い文字列表現。</summary>
        static string Display(SerializedProperty p)
        {
            switch (p.propertyType)
            {
                case SerializedPropertyType.Boolean: return p.boolValue ? "ON" : "OFF";
                case SerializedPropertyType.Integer: return p.longValue.ToString(Inv);
                case SerializedPropertyType.Float: return Num(p.numericType == SerializedPropertyNumericType.Double ? p.doubleValue : p.floatValue);
                case SerializedPropertyType.String: return p.stringValue.Length > 80 ? p.stringValue.Substring(0, 80) + "…" : p.stringValue;
                case SerializedPropertyType.Enum:
                {
                    ResolveField(p, out var t);
                    if (IsFlags(t))
                    {
                        var mask = p.enumValueFlag;
                        var names = Enum.GetNames(t).Where(n =>
                        {
                            var v = Convert.ToInt64(Enum.Parse(t, n));
                            return v != 0 && (v & (v - 1)) == 0 && (mask & v) != 0;
                        });
                        var joined = string.Join(" | ", names);
                        return joined.Length > 0 ? joined : "(なし)";
                    }
                    var idx = p.enumValueIndex;
                    return idx >= 0 && idx < p.enumDisplayNames.Length ? p.enumDisplayNames[idx] : idx.ToString(Inv);
                }
                case SerializedPropertyType.ObjectReference:
                    return p.objectReferenceValue != null ? p.objectReferenceValue.name : "(なし)";
                case SerializedPropertyType.Vector2: return p.vector2Value.ToString();
                case SerializedPropertyType.Vector3: return p.vector3Value.ToString();
                case SerializedPropertyType.Vector4: return p.vector4Value.ToString();
                case SerializedPropertyType.Vector2Int: return p.vector2IntValue.ToString();
                case SerializedPropertyType.Vector3Int: return p.vector3IntValue.ToString();
                case SerializedPropertyType.Color: return "#" + ColorUtility.ToHtmlStringRGBA(p.colorValue);
                case SerializedPropertyType.Generic: return p.isArray ? "要素数 " + p.arraySize : "";
                default: return "";
            }
        }

        static string History(Msg m)
        {
            var sb = new StringBuilder("{\"t\":\"history\",\"id\":");
            Q(sb, m.id);
            sb.Append(",\"items\":[");
            var list = RemoteHistory.instance.entries;
            for (var i = list.Count - 1; i >= 0; i--)
            {
                var e = list[i];
                if (i < list.Count - 1) sb.Append(',');
                sb.Append("{\"seq\":").Append(e.seq);
                sb.Append(",\"time\":").Append(e.time);
                sb.Append(",\"op\":"); Q(sb, e.op);
                sb.Append(",\"asset\":"); Q(sb, e.asset);
                sb.Append(",\"assetLabel\":"); Q(sb, e.assetLabel);
                sb.Append(",\"label\":"); Q(sb, e.label);
                sb.Append(",\"oldValue\":"); Q(sb, e.oldValue);
                sb.Append(",\"newValue\":"); Q(sb, e.newValue);
                sb.Append(",\"undoable\":").Append(e.undoable && !e.undone ? "true" : "false");
                sb.Append(",\"undone\":").Append(e.undone ? "true" : "false");
                sb.Append('}');
            }
            return sb.Append("]}").ToString();
        }

        /// <summary>
        /// 履歴の1件を取り消す。変更前のアセット全体に戻すため、同じアセットへのそれ以降の変更もまとめて戻る。
        /// 取り消し自体も履歴に残るので、さらに取り消せば元に戻せる。
        /// </summary>
        static string UndoEntry(Msg m)
        {
            var h = RemoteHistory.instance;
            var e = h.Find(m.index);
            if (e == null) return Ack(m, false, "履歴が見つかりません", null);
            if (!e.undoable || e.undone) return Ack(m, false, "この変更は元に戻せません", null);

            if (e.op == "dup")
            {
                if (AssetDatabase.LoadMainAssetAtPath(e.asset) != null && !AssetDatabase.MoveAssetToTrash(e.asset))
                    return Ack(m, false, "複製したアセットを削除できませんでした", null);
                e.undone = true;
                h.Add(new HistoryEntry { op = "undo", asset = e.asset, assetLabel = e.assetLabel, label = "取り消し: " + e.label, oldValue = e.newValue, newValue = "削除", undoable = false });
                return Ack(m, true, null, null);
            }

            var o = LoadAllowed(e.asset, out var err);
            if (o == null) return Ack(m, false, err, null);

            var current = EditorJsonUtility.ToJson(o);
            EditorJsonUtility.FromJsonOverwrite(e.snapshot, o);
            EditorUtility.SetDirty(o);
            ScheduleSave();

            foreach (var later in h.entries)
                if (later.seq >= e.seq && later.asset == e.asset && !later.undone && later.op != "trash") later.undone = true;
            h.Add(new HistoryEntry
            {
                op = "undo", asset = e.asset, assetLabel = e.assetLabel,
                label = "取り消し: " + e.label, oldValue = e.newValue, newValue = e.oldValue,
                snapshot = current, undoable = true,
            });
            return Ack(m, true, null, null);
        }
    }
}

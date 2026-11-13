using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace BasaltConfigTool
{
    public enum JsonKind { Object, Array, String, Number, Bool, Null }

    /// <summary>
    /// 有序 JSON 树上的一个节点。字段保持读进来的顺序，
    /// 并且各自记住源文件里它前面有没有空行，
    /// 这样保存时能把文件按用户原本写的排版还回去。
    /// </summary>
    public sealed class JsonNode
    {
        public JsonKind Kind;
        public List<JsonMember> Members;   // 对象
        public List<JsonNode> Items;       // 数组
        public string Text;                // 字符串值，或数字的原始文本
        public bool Flag;                  // 布尔值

        public static JsonNode NewObject() { return new JsonNode { Kind = JsonKind.Object, Members = new List<JsonMember>() }; }
        public static JsonNode NewArray() { return new JsonNode { Kind = JsonKind.Array, Items = new List<JsonNode>() }; }
        public static JsonNode NewString(string s) { return new JsonNode { Kind = JsonKind.String, Text = s ?? "" }; }
        public static JsonNode NewNumber(string raw) { return new JsonNode { Kind = JsonKind.Number, Text = raw }; }
        public static JsonNode NewBool(bool b) { return new JsonNode { Kind = JsonKind.Bool, Flag = b }; }
        public static JsonNode NewNull() { return new JsonNode { Kind = JsonKind.Null }; }

        public JsonNode Get(string key)
        {
            if (Kind != JsonKind.Object) return null;
            for (int i = 0; i < Members.Count; i++)
                if (string.Equals(Members[i].Key, key, StringComparison.Ordinal)) return Members[i].Value;
            return null;
        }

        public bool Has(string key) { return Get(key) != null; }

        public void Set(string key, JsonNode value)
        {
            if (Kind != JsonKind.Object) throw new InvalidOperationException("不是对象");
            for (int i = 0; i < Members.Count; i++)
            {
                if (string.Equals(Members[i].Key, key, StringComparison.Ordinal))
                {
                    Members[i].Value = value;
                    return;
                }
            }
            Members.Add(new JsonMember { Key = key, Value = value, BlankBefore = false });
        }

        public bool Remove(string key)
        {
            if (Kind != JsonKind.Object) return false;
            for (int i = 0; i < Members.Count; i++)
            {
                if (string.Equals(Members[i].Key, key, StringComparison.Ordinal))
                {
                    Members.RemoveAt(i);
                    return true;
                }
            }
            return false;
        }

        /// <summary>标量返回显示用的文本，容器返回一句简要概括。</summary>
        public string ToDisplay()
        {
            switch (Kind)
            {
                case JsonKind.String: return Text;
                case JsonKind.Number: return Text;
                case JsonKind.Bool: return Flag ? "true" : "false";
                case JsonKind.Null: return "null";
                case JsonKind.Array: return "[" + Items.Count + " 项]";
                default: return "{" + Members.Count + " 个字段}";
            }
        }
    }

    public sealed class JsonMember
    {
        public string Key;
        public JsonNode Value;
        public bool BlankBefore;
    }

    public sealed class JsonParseException : Exception
    {
        public int Offset;
        public JsonParseException(string message, int offset) : base(message) { Offset = offset; }
    }

    public static class Json
    {
        public static JsonNode Parse(string text)
        {
            int i = 0;
            SkipWs(text, ref i);
            JsonNode n = ParseValue(text, ref i);
            SkipWs(text, ref i);
            if (i < text.Length) throw new JsonParseException("末尾有多余内容", i);
            return n;
        }

        /// <summary>
        /// 跳过空白，并返回跨过了几个换行，
        /// 这样对象解析器才能区分“两个字段之间隔着空行”和“只是普通换行”。
        /// </summary>
        static int SkipWs(string s, ref int i)
        {
            int newlines = 0;
            while (i < s.Length)
            {
                char c = s[i];
                if (c == '\n') { newlines++; i++; continue; }
                if (c == ' ' || c == '\t' || c == '\r') { i++; continue; }
                break;
            }
            return newlines;
        }

        static JsonNode ParseValue(string s, ref int i)
        {
            if (i >= s.Length) throw new JsonParseException("意外结束", i);
            char c = s[i];
            switch (c)
            {
                case '{': return ParseObject(s, ref i);
                case '[': return ParseArray(s, ref i);
                case '"': return JsonNode.NewString(ParseString(s, ref i));
                case 't':
                    Expect(s, ref i, "true");
                    return JsonNode.NewBool(true);
                case 'f':
                    Expect(s, ref i, "false");
                    return JsonNode.NewBool(false);
                case 'n':
                    Expect(s, ref i, "null");
                    return JsonNode.NewNull();
                default:
                    if (c == '-' || (c >= '0' && c <= '9')) return ParseNumber(s, ref i);
                    throw new JsonParseException("无法识别的值起始字符 '" + c + "'", i);
            }
        }

        static void Expect(string s, ref int i, string word)
        {
            if (i + word.Length > s.Length || string.CompareOrdinal(s, i, word, 0, word.Length) != 0)
                throw new JsonParseException("期望 '" + word + "'", i);
            i += word.Length;
        }

        static JsonNode ParseObject(string s, ref int i)
        {
            JsonNode o = JsonNode.NewObject();
            i++; // 左花括号
            int newlinesBefore = SkipWs(s, ref i);
            if (i < s.Length && s[i] == '}') { i++; return o; }

            while (true)
            {
                // `newlinesBefore` 记录的是走到这个键之前跨了几个换行：
                // 两个或更多，就说明用户在这个键上面留了一行空行。
                JsonNode keyNode = ParseValue(s, ref i);
                if (keyNode.Kind != JsonKind.String) throw new JsonParseException("对象的键必须是字符串", i);
                SkipWs(s, ref i);
                if (i >= s.Length || s[i] != ':') throw new JsonParseException("键之后缺少 ':'", i);
                i++;
                SkipWs(s, ref i);
                JsonNode val = ParseValue(s, ref i);
                o.Members.Add(new JsonMember
                {
                    Key = keyNode.Text,
                    Value = val,
                    BlankBefore = newlinesBefore >= 2
                });

                newlinesBefore = SkipWs(s, ref i);
                if (i >= s.Length) throw new JsonParseException("对象没有闭合", i);
                if (s[i] == ',')
                {
                    i++;
                    newlinesBefore = SkipWs(s, ref i);
                    continue;
                }
                if (s[i] == '}') { i++; return o; }
                throw new JsonParseException("对象里缺少 ',' 或 '}'", i);
            }
        }

        static JsonNode ParseArray(string s, ref int i)
        {
            JsonNode a = JsonNode.NewArray();
            i++; // 左方括号
            SkipWs(s, ref i);
            if (i < s.Length && s[i] == ']') { i++; return a; }
            while (true)
            {
                SkipWs(s, ref i);
                a.Items.Add(ParseValue(s, ref i));
                SkipWs(s, ref i);
                if (i >= s.Length) throw new JsonParseException("数组没有闭合", i);
                if (s[i] == ',') { i++; continue; }
                if (s[i] == ']') { i++; return a; }
                throw new JsonParseException("数组里缺少 ',' 或 ']'", i);
            }
        }

        static string ParseString(string s, ref int i)
        {
            StringBuilder sb = new StringBuilder();
            i++; // 开头的引号
            while (true)
            {
                if (i >= s.Length) throw new JsonParseException("字符串没有闭合", i);
                char c = s[i];
                if (c == '"') { i++; return sb.ToString(); }
                if (c == '\\')
                {
                    i++;
                    if (i >= s.Length) throw new JsonParseException("转义不完整", i);
                    char e = s[i];
                    switch (e)
                    {
                        case '"': sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        case '/': sb.Append('/'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case 'u':
                            if (i + 4 >= s.Length) throw new JsonParseException("\\u 转义不完整", i);
                            {
                                string hex = s.Substring(i + 1, 4);
                                int code;
                                if (!int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out code))
                                    throw new JsonParseException("\\u 转义不是合法的十六进制", i);
                                sb.Append((char)code);
                                i += 4;
                            }
                            break;
                        default: throw new JsonParseException("不支持的转义 '\\" + e + "'", i);
                    }
                    i++;
                    continue;
                }
                sb.Append(c);
                i++;
            }
        }

        static JsonNode ParseNumber(string s, ref int i)
        {
            int start = i;
            if (i < s.Length && (s[i] == '-' || s[i] == '+')) i++;
            while (i < s.Length && (char.IsDigit(s[i]) || s[i] == '.' || s[i] == 'e' || s[i] == 'E' || s[i] == '+' || s[i] == '-')) i++;
            string raw = s.Substring(start, i - start);
            double d;
            if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out d))
                throw new JsonParseException("不是合法的数字 '" + raw + "'", start);
            return JsonNode.NewNumber(raw);
        }

        public static string Serialize(JsonNode root)
        {
            StringBuilder sb = new StringBuilder();
            Write(sb, root, 0);
            sb.Append('\n');
            return sb.ToString();
        }

        static void Indent(StringBuilder sb, int depth)
        {
            for (int k = 0; k < depth; k++) sb.Append("  ");
        }

        static void Write(StringBuilder sb, JsonNode n, int depth)
        {
            switch (n.Kind)
            {
                case JsonKind.Object:
                    if (n.Members.Count == 0) { sb.Append("{}"); return; }
                    sb.Append("{\n");
                    for (int i = 0; i < n.Members.Count; i++)
                    {
                        if (n.Members[i].BlankBefore && i > 0) sb.Append('\n');
                        Indent(sb, depth + 1);
                        sb.Append(Quote(n.Members[i].Key));
                        sb.Append(": ");
                        Write(sb, n.Members[i].Value, depth + 1);
                        if (i < n.Members.Count - 1) sb.Append(',');
                        sb.Append('\n');
                    }
                    Indent(sb, depth);
                    sb.Append('}');
                    return;
                case JsonKind.Array:
                    if (n.Items.Count == 0) { sb.Append("[]"); return; }
                    // 短的标量数组保持写在一行里
                    bool allScalar = true;
                    for (int i = 0; i < n.Items.Count; i++)
                    {
                        JsonKind k = n.Items[i].Kind;
                        if (k == JsonKind.Object || k == JsonKind.Array) { allScalar = false; break; }
                    }
                    if (allScalar && n.Items.Count <= 8)
                    {
                        sb.Append('[');
                        for (int i = 0; i < n.Items.Count; i++)
                        {
                            if (i > 0) sb.Append(", ");
                            Write(sb, n.Items[i], depth);
                        }
                        sb.Append(']');
                        return;
                    }
                    sb.Append("[\n");
                    for (int i = 0; i < n.Items.Count; i++)
                    {
                        Indent(sb, depth + 1);
                        Write(sb, n.Items[i], depth + 1);
                        if (i < n.Items.Count - 1) sb.Append(',');
                        sb.Append('\n');
                    }
                    Indent(sb, depth);
                    sb.Append(']');
                    return;
                case JsonKind.String:
                    sb.Append(Quote(n.Text));
                    return;
                case JsonKind.Number:
                    sb.Append(n.Text);
                    return;
                case JsonKind.Bool:
                    sb.Append(n.Flag ? "true" : "false");
                    return;
                default:
                    sb.Append("null");
                    return;
            }
        }

        static string Quote(string s)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append('"');
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
            return sb.ToString();
        }
    }
}

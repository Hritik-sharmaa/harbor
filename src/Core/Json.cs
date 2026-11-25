using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Harbor
{
    /// <summary>
    /// A small JSON reader and writer that works the same on .NET Framework and .NET 8 (JavaScriptSerializer only exists
    /// on Windows). Parse gives back what JavaScriptSerializer.DeserializeObject does: objects as Dictionary&lt;string, object&gt;,
    /// arrays as object[], numbers as int, long, decimal or double, plus string, bool and null.
    /// </summary>
    static class Json
    {
        public static object Parse(string s)
        {
            if (s == null) return null;
            int i = 0;
            Ws(s, ref i);
            if (i >= s.Length) return null;
            object v = Value(s, ref i, 0);
            Ws(s, ref i);
            if (i < s.Length) throw new FormatException("JSON: unexpected '" + s[i] + "' at " + i);
            return v;
        }

        /// <summary>The top-level object, or null when the text isn't one.</summary>
        public static Dictionary<string, object> Object(string s) { return Parse(s) as Dictionary<string, object>; }

        /// <summary>A top-level array of objects (non-objects are skipped), or null.</summary>
        public static List<Dictionary<string, object>> Objects(string s)
        {
            var a = Parse(s) as object[];
            if (a == null) return null;
            var l = new List<Dictionary<string, object>>();
            foreach (var o in a) { var d = o as Dictionary<string, object>; if (d != null) l.Add(d); }
            return l;
        }

        static void Ws(string s, ref int i) { while (i < s.Length && (s[i] == ' ' || s[i] == '\t' || s[i] == '\n' || s[i] == '\r')) i++; }

        static object Value(string s, ref int i, int depth)
        {
            if (depth > 200) throw new FormatException("JSON: nested too deeply");
            if (i >= s.Length) throw new FormatException("JSON: unexpected end");
            char c = s[i];
            if (c == '{')
            {
                i++;
                var d = new Dictionary<string, object>();
                Ws(s, ref i);
                if (i < s.Length && s[i] == '}') { i++; return d; }
                while (true)
                {
                    Ws(s, ref i);
                    if (i >= s.Length || s[i] != '"') throw new FormatException("JSON: expected a key at " + i);
                    string k = Str(s, ref i);
                    Ws(s, ref i);
                    if (i >= s.Length || s[i] != ':') throw new FormatException("JSON: expected ':' at " + i);
                    i++;
                    Ws(s, ref i);
                    d[k] = Value(s, ref i, depth + 1);
                    Ws(s, ref i);
                    if (i < s.Length && s[i] == ',') { i++; continue; }
                    if (i < s.Length && s[i] == '}') { i++; return d; }
                    throw new FormatException("JSON: expected ',' or '}' at " + i);
                }
            }
            if (c == '[')
            {
                i++;
                var l = new List<object>();
                Ws(s, ref i);
                if (i < s.Length && s[i] == ']') { i++; return l.ToArray(); }
                while (true)
                {
                    Ws(s, ref i);
                    l.Add(Value(s, ref i, depth + 1));
                    Ws(s, ref i);
                    if (i < s.Length && s[i] == ',') { i++; continue; }
                    if (i < s.Length && s[i] == ']') { i++; return l.ToArray(); }
                    throw new FormatException("JSON: expected ',' or ']' at " + i);
                }
            }
            if (c == '"') return Str(s, ref i);
            if (Word(s, ref i, "true")) return true;
            if (Word(s, ref i, "false")) return false;
            if (Word(s, ref i, "null")) return null;
            return Num(s, ref i);
        }

        static bool Word(string s, ref int i, string w)
        {
            if (string.CompareOrdinal(s, i, w, 0, w.Length) != 0) return false;
            i += w.Length;
            return true;
        }

        static object Num(string s, ref int i)
        {
            int start = i;
            bool frac = false;
            if (i < s.Length && s[i] == '-') i++;
            while (i < s.Length)
            {
                char c = s[i];
                if (c >= '0' && c <= '9') i++;
                else if (c == '.' || c == 'e' || c == 'E' || c == '+' || c == '-') { frac = true; i++; }
                else break;
            }
            string t = s.Substring(start, i - start);
            if (t.Length == 0 || t == "-") throw new FormatException("JSON: unexpected '" + (start < s.Length ? s[start] : ' ') + "' at " + start);
            var inv = CultureInfo.InvariantCulture;
            if (!frac)
            {
                int iv; long lv;
                if (int.TryParse(t, NumberStyles.AllowLeadingSign, inv, out iv)) return iv;
                if (long.TryParse(t, NumberStyles.AllowLeadingSign, inv, out lv)) return lv;
            }
            decimal dv;
            if (t.IndexOf('e') < 0 && t.IndexOf('E') < 0 && decimal.TryParse(t, NumberStyles.Float, inv, out dv)) return dv;
            return double.Parse(t, NumberStyles.Float, inv);
        }

        static string Str(string s, ref int i)
        {
            i++; // opening quote
            var sb = new StringBuilder();
            while (i < s.Length)
            {
                char c = s[i++];
                if (c == '"') return sb.ToString();
                if (c != '\\') { sb.Append(c); continue; }
                if (i >= s.Length) break;
                char e = s[i++];
                switch (e)
                {
                    case 'n': sb.Append('\n'); break;
                    case 't': sb.Append('\t'); break;
                    case 'r': sb.Append('\r'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'u':
                        if (i + 4 > s.Length) throw new FormatException("JSON: bad \\u escape");
                        sb.Append((char)int.Parse(s.Substring(i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                        i += 4;
                        break;
                    default: sb.Append(e); break; // \" \\ \/
                }
            }
            throw new FormatException("JSON: unterminated string");
        }

        // ------------------------------------------------------------------ writing

        public static string Write(object v)
        {
            var sb = new StringBuilder();
            Write(sb, v, 0);
            return sb.ToString();
        }

        static void Write(StringBuilder sb, object v, int depth)
        {
            if (depth > 200) throw new FormatException("JSON: nested too deeply");
            if (v == null) { sb.Append("null"); return; }
            if (v is string) { Quote(sb, (string)v); return; }
            if (v is bool) { sb.Append((bool)v ? "true" : "false"); return; }
            if (v is char) { Quote(sb, v.ToString()); return; }
            if (v is DateTime) { Quote(sb, ((DateTime)v).ToString("o", CultureInfo.InvariantCulture)); return; }
            if (v is TimeSpan) { sb.Append(((TimeSpan)v).TotalSeconds.ToString("R", CultureInfo.InvariantCulture)); return; }
            if (v is double || v is float)
            {
                double d = Convert.ToDouble(v, CultureInfo.InvariantCulture);
                sb.Append(double.IsNaN(d) || double.IsInfinity(d) ? "null" : d.ToString("R", CultureInfo.InvariantCulture));
                return;
            }
            if (v is int || v is long || v is short || v is byte || v is uint || v is ulong || v is ushort || v is sbyte || v is decimal)
            {
                sb.Append(Convert.ToString(v, CultureInfo.InvariantCulture));
                return;
            }
            if (v is Enum) { Quote(sb, v.ToString()); return; }
            var dict = v as IDictionary;
            if (dict != null)
            {
                sb.Append('{');
                bool first = true;
                foreach (DictionaryEntry kv in dict)
                {
                    if (!first) sb.Append(',');
                    first = false;
                    Quote(sb, Convert.ToString(kv.Key, CultureInfo.InvariantCulture));
                    sb.Append(':');
                    Write(sb, kv.Value, depth + 1);
                }
                sb.Append('}');
                return;
            }
            var list = v as IEnumerable;
            if (list != null)
            {
                sb.Append('[');
                bool first = true;
                foreach (var o in list)
                {
                    if (!first) sb.Append(',');
                    first = false;
                    Write(sb, o, depth + 1);
                }
                sb.Append(']');
                return;
            }
            Quote(sb, Convert.ToString(v, CultureInfo.InvariantCulture));
        }

        static void Quote(StringBuilder sb, string s)
        {
            sb.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }
    }
}

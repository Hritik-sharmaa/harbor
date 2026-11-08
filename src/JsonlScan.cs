using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace Harbor
{
    /// <summary>
    /// Reads Claude Code's transcripts (JSON lines, often hundreds of MB a week) without turning them into strings.
    /// Lines are found as bytes in one reused buffer, and only the few small fields Harbor needs (a timestamp, a model,
    /// token counts) are decoded. Lines that don't matter are never decoded at all, so a big transcript costs a small
    /// buffer instead of gigabytes of short-lived strings (which kept .NET's large-object heap busy and grew memory).
    /// </summary>
    static class JsonlScan
    {
        public delegate void LineFn(byte[] b, int start, int end);

        const int MaxLine = 8 << 20; // longer lines (a pasted image, a huge tool output) are skipped: they carry no usage

        /// <summary>Calls 'line' for each complete line after 'offset'; returns where the next read should start.</summary>
        public static long Read(string path, long offset, LineFn line)
        {
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                fs.Seek(offset, SeekOrigin.Begin);
                var buf = new byte[256 << 10];
                int have = 0, n;
                long pos = offset; // file position of buf[0]
                bool skipping = false;
                while ((n = fs.Read(buf, have, buf.Length - have)) > 0)
                {
                    have += n;
                    int start = 0, nl;
                    if (skipping)
                    {
                        nl = Array.IndexOf(buf, (byte)'\n', 0, have);
                        if (nl < 0) { pos += have; have = 0; continue; }
                        skipping = false;
                        start = nl + 1;
                    }
                    while ((nl = Array.IndexOf(buf, (byte)'\n', start, have - start)) >= 0) { line(buf, start, nl); start = nl + 1; }
                    int rest = have - start;
                    pos += start;
                    if (rest == buf.Length) // one line fills the whole buffer
                    {
                        if (buf.Length >= MaxLine) { skipping = true; pos += rest; have = 0; continue; }
                        Array.Resize(ref buf, buf.Length * 2);
                    }
                    else Buffer.BlockCopy(buf, start, buf, 0, rest);
                    have = rest;
                }
                return pos;
            }
        }

        public static byte[] Bytes(string s) { return Encoding.UTF8.GetBytes(s); }

        /// <summary>Where 'pat' first starts in b[start..end), or -1.</summary>
        public static int Find(byte[] b, int start, int end, byte[] pat)
        {
            byte first = pat[0];
            for (int i = start; i <= end - pat.Length; i++)
            {
                i = Array.IndexOf(b, first, i, end - pat.Length + 1 - i);
                if (i < 0) return -1;
                int k = 1;
                while (k < pat.Length && b[i + k] == pat[k]) k++;
                if (k == pat.Length) return i;
            }
            return -1;
        }

        public static bool Has(byte[] b, int start, int end, byte[] pat) { return Find(b, start, end, pat) >= 0; }

        /// <summary>The string value right after 'key' (which ends with the opening quote), JSON escapes undone.</summary>
        public static string Str(byte[] b, int start, int end, byte[] key)
        {
            int i = Find(b, start, end, key);
            if (i < 0) return null;
            int s = i + key.Length, e = s;
            bool escaped = false;
            while (e < end && (b[e] != (byte)'"' || IsEscaped(b, s, e))) { if (b[e] == (byte)'\\') escaped = true; e++; }
            if (e >= end || e - s > 4096) return null;
            string v = Encoding.UTF8.GetString(b, s, e - s);
            return escaped ? Regex.Unescape(v) : v;
        }

        static bool IsEscaped(byte[] b, int from, int quote)
        {
            int n = 0;
            for (int i = quote - 1; i >= from && b[i] == (byte)'\\'; i--) n++;
            return (n & 1) == 1;
        }

        /// <summary>The whole number right after 'key' (e.g. "input_tokens":), or 0.</summary>
        public static long Num(byte[] b, int start, int end, byte[] key)
        {
            int i = Find(b, start, end, key);
            if (i < 0) return 0;
            long v = 0;
            for (int k = i + key.Length; k < end && b[k] >= (byte)'0' && b[k] <= (byte)'9'; k++) v = v * 10 + (b[k] - '0');
            return v;
        }
    }
}

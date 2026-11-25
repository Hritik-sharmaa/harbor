using System;
using System.Collections.Generic;
using System.IO;

namespace Harbor
{
    /// <summary>The shared part of ClaudeBridge: where Claude Code keeps its settings and transcripts.</summary>
    static partial class ClaudeBridge
    {
        /// <summary>
        /// Every Claude Code settings folder on this PC: %USERPROFILE%\.claude plus the per-editor / per-account ones
        /// (.claude-vscode, .claude-antigravity, .claude-work... set through CLAUDE_CONFIG_DIR), once they're in use.
        /// </summary>
        public static List<string> ConfigDirs()
        {
            var dirs = new List<string>();
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var candidates = new List<string> { Path.Combine(home, ".claude") };
            try { foreach (var d in Directory.GetDirectories(home, ".claude-*")) candidates.Add(d); } catch { }
            string env = Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR");
            if (!string.IsNullOrEmpty(env)) candidates.Add(Environment.ExpandEnvironmentVariables(env.Replace('/', Path.DirectorySeparatorChar)));
            foreach (var d in candidates)
            {
                if (!Directory.Exists(d) || dirs.Exists(x => string.Equals(x.TrimEnd('\\', '/'), d.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase))) continue;
                bool used = string.Equals(Path.GetFileName(d), ".claude", StringComparison.OrdinalIgnoreCase)
                    || File.Exists(Path.Combine(d, ".claude.json")) || File.Exists(Path.Combine(d, ".credentials.json"))
                    || File.Exists(Path.Combine(d, "settings.json")) || Directory.Exists(Path.Combine(d, "projects"));
                if (used) dirs.Add(d); // an empty profile folder gets the hook once Claude Code starts using it
            }
            return dirs;
        }
    }
}

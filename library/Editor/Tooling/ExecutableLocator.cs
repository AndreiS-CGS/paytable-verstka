using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;

namespace CGS.PaytableLibrary.Tooling
{
    /// <summary>
    /// Finds executables by absolute path.
    ///
    /// A Unity editor launched from Finder or the Hub inherits launchd's environment, not the
    /// user's login shell — so `python3` and `git` are usually not on PATH as far as this process
    /// is concerned, even though they work perfectly in a terminal. Everything here therefore
    /// returns an absolute path or null; nothing relies on PATH lookup succeeding.
    /// </summary>
    public static class ExecutableLocator
    {
        const string LoginPathKey = "CGS.Paytable.LoginPath";

        /// <summary>
        /// Interpreter to run the tooling's Python with. The venv first: that is the one the
        /// scripts re-exec into anyway, so using it directly saves a hop and makes the window's
        /// probes measure the same interpreter the scripts will use.
        ///
        /// env_doctor.py is the exception — it must run before the venv exists, so callers pass
        /// <paramref name="allowVenv"/> = false for it and get a plain system interpreter.
        /// </summary>
        public static string FindPython(bool allowVenv = true)
        {
            var explicitPath = Environment.GetEnvironmentVariable("PAYTABLE_PYTHON");
            if (!string.IsNullOrEmpty(explicitPath) && File.Exists(explicitPath))
                return explicitPath;

            if (allowVenv && PaytablePaths.VenvExists)
                return PaytablePaths.VenvPython;

            foreach (var c in PythonCandidates())
                if (IsRealExecutable(c))
                    return c;

            return FromLoginPath("python3") ?? FromLoginPath("python");
        }

        public static IEnumerable<string> PythonCandidates()
        {
            if (PaytablePaths.IsWindows)
            {
                var localApp = Environment.GetEnvironmentVariable("LOCALAPPDATA");
                if (!string.IsNullOrEmpty(localApp))
                {
                    var progs = Path.Combine(localApp, "Programs", "Python");
                    if (Directory.Exists(progs))
                        foreach (var d in Directory.GetDirectories(progs, "Python3*"))
                            yield return Path.Combine(d, "python.exe");
                }
                foreach (var d in SafeDirs(@"C:\", "Python3*"))
                    yield return Path.Combine(d, "python.exe");
                // The py launcher is the one thing a stock Windows Python install always puts on
                // PATH, and it is how you reach an interpreter that is not where you guessed.
                var winDir = Environment.GetEnvironmentVariable("WINDIR");
                if (!string.IsNullOrEmpty(winDir))
                    yield return Path.Combine(winDir, "py.exe");
                yield break;
            }

            // Framework installs first: their path is not version-stamped by a package manager, so
            // a venv built on one is not orphaned by `brew cleanup` after a minor bump.
            foreach (var d in SafeDirs("/Library/Frameworks/Python.framework/Versions", "3.*"))
                yield return Path.Combine(d, "bin", "python3");

            yield return "/opt/homebrew/bin/python3";
            yield return "/usr/local/bin/python3";

            // ~/.pyenv/versions, never ~/.pyenv/shims: a shim is a shell script that resolves to
            // whatever pyenv currently points at, which is not a stable base for a venv.
            foreach (var d in SafeDirs(Path.Combine(PaytablePaths.Home, ".pyenv", "versions"), "*"))
                yield return Path.Combine(d, "bin", "python3");

            // Apple's is last and is only a fallback for probing — it is 3.9 and too old to build
            // the venv from.
            yield return "/usr/bin/python3";
        }

        public static string FindGit()
        {
            if (PaytablePaths.IsWindows)
            {
                foreach (var p in new[]
                         {
                             @"C:\Program Files\Git\cmd\git.exe",
                             @"C:\Program Files (x86)\Git\cmd\git.exe"
                         })
                    if (File.Exists(p)) return p;
                return FromLoginPath("git");
            }
            foreach (var p in new[] { "/opt/homebrew/bin/git", "/usr/local/bin/git", "/usr/bin/git" })
                if (File.Exists(p)) return p;
            return FromLoginPath("git");
        }

        /// <summary>
        /// A zero-length python.exe under WindowsApps is a Microsoft Store alias stub: it exists,
        /// it is on PATH, and running it opens the Store instead. File.Exists alone is a guaranteed
        /// false pass there, which is the most common Windows Python failure.
        /// </summary>
        public static bool IsRealExecutable(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return false;
            if (PaytablePaths.IsWindows &&
                path.IndexOf("WindowsApps", StringComparison.OrdinalIgnoreCase) >= 0)
                return false;
            try { return new FileInfo(path).Length > 0; }
            catch { return false; }
        }

        /// <summary>
        /// Last resort: ask a login shell what PATH really is, then look the name up in it. Cached
        /// for the session — this costs a subprocess.
        ///
        /// `-lc`, never `-lic`: an interactive shell runs the full rc file and can hang, and its
        /// startup chatter lands on stdout mixed with the answer.
        /// </summary>
        static string FromLoginPath(string exeName)
        {
            var path = SessionState.GetString(LoginPathKey, null);
            if (string.IsNullOrEmpty(path))
            {
                path = QueryLoginPath() ?? "";
                SessionState.SetString(LoginPathKey, path);
            }
            if (string.IsNullOrEmpty(path)) return null;

            var sep = PaytablePaths.IsWindows ? ';' : ':';
            foreach (var dir in path.Split(sep))
            {
                if (string.IsNullOrWhiteSpace(dir)) continue;
                var candidate = Path.Combine(dir.Trim(), exeName);
                if (IsRealExecutable(candidate)) return candidate;
            }
            return null;
        }

        static string QueryLoginPath()
        {
            if (PaytablePaths.IsWindows)
                return Environment.GetEnvironmentVariable("PATH");

            var shell = File.Exists("/bin/zsh") ? "/bin/zsh" : "/bin/bash";
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = shell,
                    // Sentinels: rc files print things ("Restored session: ..."), and without a
                    // delimiter that noise is indistinguishable from the answer.
                    Arguments = "-lc " + ProcessProbe.Quote("printf '@@%s@@' \"$PATH\""),
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                };
                using (var p = System.Diagnostics.Process.Start(psi))
                {
                    if (p == null) return null;
                    var so = p.StandardOutput.ReadToEnd();
                    if (!p.WaitForExit(4000)) { try { p.Kill(); } catch { } return null; }
                    var m = System.Text.RegularExpressions.Regex.Match(so, "@@(.*?)@@",
                        System.Text.RegularExpressions.RegexOptions.Singleline);
                    return m.Success ? m.Groups[1].Value : null;
                }
            }
            catch { return null; }
        }

        static IEnumerable<string> SafeDirs(string root, string pattern)
        {
            string[] dirs;
            try { dirs = Directory.Exists(root) ? Directory.GetDirectories(root, pattern) : new string[0]; }
            catch { yield break; }
            Array.Sort(dirs, StringComparer.Ordinal);
            Array.Reverse(dirs);   // newest version first
            foreach (var d in dirs) yield return d;
        }
    }
}

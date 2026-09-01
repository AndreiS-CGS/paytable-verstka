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
            var explicitPath = ExplicitPython();
            if (IsRealExecutable(explicitPath))
                return explicitPath;

            if (allowVenv && PaytablePaths.VenvExists)
                return PaytablePaths.VenvPython;

            foreach (var c in PythonCandidates())
                if (IsRealExecutable(c))
                    return c;

            return FromLoginPath("python3") ?? FromLoginPath("python");
        }

        /// <summary>
        /// The manual override, checked before anything is guessed: environment first, then the
        /// config file. That is the same precedence _bootstrap.py uses, so an interpreter set here
        /// is also the one the skills' scripts re-exec into — one setting, not two.
        /// </summary>
        public static string ExplicitPython()
        {
            var v = Environment.GetEnvironmentVariable("PAYTABLE_PYTHON");
            if (!string.IsNullOrEmpty(v)) return v;
            return PaytablePaths.ConfigValue("PAYTABLE_PYTHON");
        }

        /// <summary>Roots that get scanned for a Python3* folder, in preference order.</summary>
        static IEnumerable<string> WindowsInstallRoots()
        {
            var localApp = Environment.GetEnvironmentVariable("LOCALAPPDATA");
            if (!string.IsNullOrEmpty(localApp))
                yield return Path.Combine(localApp, "Programs", "Python");
            // "Install for all users" — the installer's other option, and the one this locator
            // used to miss completely.
            foreach (var v in new[] { "ProgramFiles", "ProgramFiles(x86)", "ProgramW6432" })
            {
                var root = Environment.GetEnvironmentVariable(v);
                if (!string.IsNullOrEmpty(root)) yield return root;
            }
            yield return @"C:\";
        }

        public static IEnumerable<string> PythonCandidates()
        {
            if (PaytablePaths.IsWindows)
            {
                // The launcher first: `py -0p` reports what the registry says is installed, so it
                // finds interpreters no directory guess would reach and needs nothing on PATH.
                foreach (var p in RegisteredWindowsPythons())
                    yield return p;

                foreach (var root in WindowsInstallRoots())
                    foreach (var d in SafeDirs(root, "Python3*"))
                        yield return Path.Combine(d, "python.exe");

                // Last: the launcher itself as the interpreter. It runs a script perfectly well,
                // so it is a working fallback when the real python.exe cannot be located.
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

        const string RegisteredPythonsKey = "CGS.Paytable.WinRegisteredPythons";

        /// <summary>
        /// Asks the py launcher what is installed. Cached for the session; costs a subprocess.
        ///
        /// This is the reliable way in on Windows because it does not depend on PATH. Unity
        /// snapshots the environment block when it launches, so a Python installed while Unity was
        /// already open is absent from PATH as far as this process is concerned — the single most
        /// common report of "the tool cannot see my Python", and one that looks like a bug in the
        /// tool rather than a stale variable.
        /// </summary>
        static IEnumerable<string> RegisteredWindowsPythons()
        {
            var cached = SessionState.GetString(RegisteredPythonsKey, null);
            if (cached == null)
            {
                cached = QueryPyLauncher() ?? "";
                SessionState.SetString(RegisteredPythonsKey, cached);
            }
            if (cached.Length == 0) yield break;
            foreach (var line in cached.Split('\n'))
                if (line.Trim().Length > 0) yield return line.Trim();
        }

        static string QueryPyLauncher()
        {
            var winDir = Environment.GetEnvironmentVariable("WINDIR");
            var py = string.IsNullOrEmpty(winDir) ? null : Path.Combine(winDir, "py.exe");
            // WINDIR is where a stock install always puts the launcher, so prefer the absolute
            // path; fall back to the bare name in case PATH knows better.
            if (!IsRealExecutable(py)) py = "py";

            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = py,
                    Arguments = "-0p",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                };
                using (var proc = System.Diagnostics.Process.Start(psi))
                {
                    if (proc == null) return null;
                    var so = proc.StandardOutput.ReadToEnd();
                    if (!proc.WaitForExit(6000)) { try { proc.Kill(); } catch { } return null; }

                    var found = new List<string>();
                    foreach (var line in so.Split('\n'))
                    {
                        // A line reads " -V:3.13 *        C:\Program Files\Python313\python.exe".
                        // Take everything from the drive letter to the end of the line: splitting
                        // on whitespace loses every install whose path contains a space, which is
                        // exactly what "C:\Program Files" is.
                        var m = System.Text.RegularExpressions.Regex.Match(
                            line, @"([A-Za-z]:[\\/][^\r\n]*python\.exe)\s*$",
                            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                        if (m.Success) found.Add(m.Groups[1].Value);
                    }
                    return string.Join("\n", found);
                }
            }
            catch { return null; }
        }

        /// <summary>
        /// What was searched and what turned up — the body of the message shown when nothing was
        /// found. Written per platform on purpose: the text this replaced named the Python
        /// framework, homebrew and pyenv directories on Windows too, so a Windows user was told
        /// the tool had looked in three places that cannot exist on their machine.
        /// </summary>
        public static string DescribeSearch()
        {
            var sb = new System.Text.StringBuilder();

            var ex = ExplicitPython();
            if (string.IsNullOrEmpty(ex))
                sb.AppendLine("PAYTABLE_PYTHON: not set.");
            else
                sb.AppendLine("PAYTABLE_PYTHON = " + ex +
                              (IsRealExecutable(ex) ? "" : "   <- set, but not a usable executable"));
            sb.AppendLine();

            if (PaytablePaths.IsWindows)
            {
                var registered = new List<string>(RegisteredWindowsPythons());
                sb.AppendLine("py -0p (the launcher's registry list): " +
                              (registered.Count == 0
                                  ? "nothing, or py.exe itself is absent"
                                  : registered.Count + " reported"));
                sb.AppendLine("Scanned for a Python3* folder in:");
                foreach (var root in WindowsInstallRoots())
                    sb.AppendLine("  " + (Directory.Exists(root) ? " " : "-") + " " + root);
            }
            sb.AppendLine();

            sb.AppendLine("Candidates, in the order they are tried:");
            var any = false;
            foreach (var c in PythonCandidates())
            {
                any = true;
                sb.AppendLine("  " + (IsRealExecutable(c) ? "OK      " : "absent  ") + c);
            }
            if (!any) sb.AppendLine("  (none — no Python directory was found at all)");
            sb.AppendLine();

            sb.AppendLine(PaytablePaths.IsWindows
                ? "If Python IS installed, the likeliest cause is a stale environment: Unity reads\n" +
                  "PATH once, when it starts, so an interpreter installed after that is invisible\n" +
                  "here even though it works in a fresh terminal. Restart Unity, or skip the whole\n" +
                  "question by pointing the field above straight at python.exe."
                : "Unity inherits launchd's environment rather than your login shell's, so a python3\n" +
                  "that works in a terminal can still be invisible here. Point the field above at\n" +
                  "the interpreter to settle it.");
            return sb.ToString();
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

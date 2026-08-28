using System;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace CGS.PaytableLibrary.Tooling
{
    /// <summary>
    /// Finding out whether a newer version of this package exists, and pulling it in.
    ///
    /// This has to exist because a git dependency pins the commit it first resolved into
    /// packages-lock.json and never looks again. Someone who installed the package in March is
    /// still on March's commit, and nothing tells them — the version string in package.json does
    /// not change per commit, so the Package row would read identically either way. Without this,
    /// updating means hand-editing a lock file and deleting a cache folder, which is not something
    /// to ask of everyone on a team.
    /// </summary>
    public static class PackageUpdate
    {
        public enum State { Unknown, NotGit, UpToDate, Behind, CheckFailed, NoLocalPin }

        public sealed class Result
        {
            public State State = State.Unknown;
            public string LocalHash = "";
            public string RemoteHash = "";
            public string Url = "";
            public string Ref = "";          // the #ref part, empty when the URL has none
            public string Message = "";

            public string LocalShort => Short(LocalHash);
            public string RemoteShort => Short(RemoteHash);
            static string Short(string h) => string.IsNullOrEmpty(h) ? "?" : h.Substring(0, Math.Min(12, h.Length));
        }

        static string ManifestPath =>
            Path.Combine(PaytablePaths.UnityProjectRoot ?? "", "Packages", "manifest.json");

        static string LockPath =>
            Path.Combine(PaytablePaths.UnityProjectRoot ?? "", "Packages", "packages-lock.json");

        static string CacheDir =>
            Path.Combine(PaytablePaths.UnityProjectRoot ?? "", "Library", "PackageCache");

        /// <summary>The URL the manifest asks for, which is what git must be queried about.</summary>
        public static string RequestedUrl()
        {
            try
            {
                if (!File.Exists(ManifestPath)) return "";
                var m = Regex.Match(File.ReadAllText(ManifestPath),
                    "\"" + Regex.Escape(PaytablePaths.PackageName) + "\"\\s*:\\s*\"([^\"]+)\"");
                return m.Success ? m.Groups[1].Value : "";
            }
            catch { return ""; }
        }

        /// <summary>The commit currently pinned. The lock is authoritative; the cache folder name agrees.</summary>
        public static string ResolvedHash()
        {
            try
            {
                if (!File.Exists(LockPath)) return "";
                var text = File.ReadAllText(LockPath);
                var block = Regex.Match(text,
                    "\"" + Regex.Escape(PaytablePaths.PackageName) + "\"\\s*:\\s*\\{(.*?)\\}",
                    RegexOptions.Singleline);
                if (!block.Success) return "";
                var h = Regex.Match(block.Groups[1].Value, "\"hash\"\\s*:\\s*\"([0-9a-f]+)\"");
                return h.Success ? h.Groups[1].Value : "";
            }
            catch { return ""; }
        }

        /// <summary>
        /// Asks the remote what the requested ref points at now, using the machine's own git
        /// credentials. Never prompts: GIT_TERMINAL_PROMPT is off in ProcessProbe, so a missing
        /// credential fails fast instead of hanging the window on an invisible password prompt.
        /// </summary>
        public static bool StartCheck(PaytableToolsWindow window, Action<Result> done)
        {
            var result = new Result();

            if (PaytablePaths.Source != UnityEditor.PackageManager.PackageSource.Git)
            {
                result.State = State.NotGit;
                result.Message = "This project uses a local or embedded copy of the package, so " +
                                 "there is nothing to fetch — edits to it are already live.";
                done(result);
                return false;
            }

            var raw = RequestedUrl();
            if (string.IsNullOrEmpty(raw))
            {
                result.State = State.CheckFailed;
                result.Message = "No " + PaytablePaths.PackageName + " entry found in manifest.json.";
                done(result);
                return false;
            }

            // "https://host/repo.git?path=/library#v0.2.0" -> url, ref
            var url = raw;
            var hash = url.IndexOf('#');
            if (hash >= 0) { result.Ref = url.Substring(hash + 1); url = url.Substring(0, hash); }
            var q = url.IndexOf('?');
            if (q >= 0) url = url.Substring(0, q);
            result.Url = url;
            result.LocalHash = ResolvedHash();

            var git = ExecutableLocator.FindGit();
            if (git == null)
            {
                result.State = State.CheckFailed;
                result.Message = "git was not found. Unity does not inherit a login shell PATH, so " +
                                 "a git that works in your terminal can still be invisible here.";
                done(result);
                return false;
            }

            var args = string.IsNullOrEmpty(result.Ref)
                ? new[] { "ls-remote", url, "HEAD" }
                : new[] { "ls-remote", url, result.Ref };

            return window.StartProcess(git, args, null, 45, p =>
            {
                if (p.Status != ProcessProbe.State.Exited || p.ExitCode != 0)
                {
                    result.State = State.CheckFailed;
                    result.Message = p.Status == ProcessProbe.State.TimedOut
                        ? "git ls-remote timed out — offline, or the credential helper is waiting " +
                          "on something it cannot ask for."
                        : "git ls-remote failed:\n" + p.OutputText;
                    done(result);
                    return;
                }
                var m = Regex.Match(p.StdOutText.Trim(), "^([0-9a-f]{7,40})");
                if (!m.Success)
                {
                    result.State = State.CheckFailed;
                    result.Message = string.IsNullOrEmpty(result.Ref)
                        ? "The remote returned no HEAD."
                        : $"The remote has no ref \"{result.Ref}\".";
                    done(result);
                    return;
                }
                result.RemoteHash = m.Groups[1].Value;
                if (string.IsNullOrEmpty(result.LocalHash))
                {
                    // Unity writes the lock entry only after a resolve finishes, so between
                    // clearing the pin and that write there is nothing to compare against.
                    // Comparing anyway would read as "behind" every time and offer an update that
                    // fetches what is already there.
                    result.State = State.NoLocalPin;
                    result.Message = "packages-lock.json has no entry for this package yet — Unity " +
                                     "writes it once a resolve completes. Nothing to compare " +
                                     "against; re-check in a moment.";
                }
                else
                {
                    result.State = string.Equals(result.RemoteHash, result.LocalHash,
                                                 StringComparison.OrdinalIgnoreCase)
                        ? State.UpToDate
                        : State.Behind;
                }
                done(result);
            });
        }

        /// <summary>
        /// Forgets the pin and drops the cached copy, then asks Unity to resolve again.
        ///
        /// Both steps are needed: Client.Resolve() on its own only re-reads the lock, so the pin
        /// would simply be restored. Safe to run from inside the package being replaced — the
        /// compiled assembly lives in Library/ScriptAssemblies, not in the cache folder, and
        /// Resolve() is asynchronous, so this method returns before the domain reload.
        /// </summary>
        public static bool Apply(out string report)
        {
            report = "";
            var sb = new System.Text.StringBuilder();
            try
            {
                if (File.Exists(LockPath))
                {
                    var text = File.ReadAllText(LockPath);
                    var block = Regex.Match(text,
                        ",?\\s*\"" + Regex.Escape(PaytablePaths.PackageName) + "\"\\s*:\\s*\\{.*?\\}",
                        RegexOptions.Singleline);
                    if (block.Success)
                    {
                        File.Copy(LockPath, LockPath + ".bak", true);
                        File.WriteAllText(LockPath, text.Remove(block.Index, block.Length));
                        sb.AppendLine("removed the lock pin (backup: packages-lock.json.bak)");
                    }
                    else sb.AppendLine("no lock entry to remove");
                }

                if (Directory.Exists(CacheDir))
                {
                    foreach (var d in Directory.GetDirectories(CacheDir, PaytablePaths.PackageName + "@*"))
                    {
                        // Belt and braces before a recursive delete: inside this project's
                        // Library/PackageCache, and named for this package.
                        if (!d.StartsWith(CacheDir, StringComparison.Ordinal)) continue;
                        Directory.Delete(d, true);
                        sb.AppendLine("removed " + Path.GetFileName(d));
                    }
                }
            }
            catch (Exception e)
            {
                report = "Update failed before resolving: " + e.Message +
                         "\nThe lock backup, if one was written, is packages-lock.json.bak.";
                return false;
            }

            UnityEditor.PackageManager.Client.Resolve();
            sb.AppendLine("asked Unity to resolve — it will reload once the fetch completes.");
            report = sb.ToString();
            return true;
        }
    }
}

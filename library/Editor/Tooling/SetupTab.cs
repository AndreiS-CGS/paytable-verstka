using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace CGS.PaytableLibrary.Tooling
{
    /// <summary>
    /// The checklist. Replaces SETUP.md's nine manual steps for everything that can be checked
    /// mechanically, and states the rest plainly instead of pretending it can be automated.
    ///
    /// Nothing here runs on window open except free filesystem reads. A Setup tab that spawns
    /// processes on every domain reload would be worse than the document it replaces.
    /// </summary>
    public sealed class SetupTab
    {
        readonly PaytableToolsWindow _w;
        Dictionary<string, string> _env = new Dictionary<string, string>();
        List<string> _envLines = new List<string>();
        string _confluenceEmail;
        string _patInput = "";
        PackageUpdate.Result _update;
        bool _installUserWide;

        public SetupTab(PaytableToolsWindow w)
        {
            _w = w;
            // Seed from the config FILE, not from EditorPrefs. The file is what the Python scripts
            // read, so it is the only real state; EditorPrefs is a mirror, and a mirror that comes
            // back empty after a domain reload is worse than no mirror — the empty field then gets
            // written back over a perfectly good setting on the next Save.
            _confluenceEmail = ReadConfig("CONFLUENCE_EMAIL");
            if (string.IsNullOrEmpty(_confluenceEmail))
                _confluenceEmail = EditorPrefs.GetString("CGS.Paytable.Setup.ConfluenceEmail", "");
            _installUserWide = EditorPrefs.GetBool("CGS.Paytable.Setup.InstallUserWide", false);
        }

        /// <summary>Reads one key out of the flat config file we write ourselves.</summary>
        static string ReadConfig(string key)
        {
            try
            {
                var p = PaytablePaths.ToolsConfigFile;
                if (p == null || !File.Exists(p)) return "";
                var m = System.Text.RegularExpressions.Regex.Match(
                    File.ReadAllText(p), "\"" + key + "\"\\s*:\\s*\"([^\"]*)\"");
                return m.Success ? m.Groups[1].Value : "";
            }
            catch { return ""; }
        }

        public void EnsureChecks()
        {
            if (_w.Setup.Checks.Count > 0) return;
            _w.Setup.Checks.AddRange(new[]
            {
                new SetupCheck { Id = CheckId.Package,    Title = "Package resolved" },
                new SetupCheck { Id = CheckId.Python,     Title = "Python environment" },
                new SetupCheck { Id = CheckId.Skills,     Title = "Skills installed" },
                new SetupCheck { Id = CheckId.Confluence, Title = "Confluence access" },
                new SetupCheck { Id = CheckId.UnityMcp,   Title = "unityMCP" },
            });
            CheckPackage();
            CheckSkills();
            CheckUnityMcp();
        }

        // ── drawing ─────────────────────────────────────────────────────────

        public void Draw()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(_w.Busy))
                {
                    if (GUILayout.Button("Re-check all", GUILayout.Width(110))) RunAllChecks();
                }
                GUILayout.FlexibleSpace();
                if (_w.Setup.LastFullRunAt > 0)
                {
                    var age = EditorApplication.timeSinceStartup - _w.Setup.LastFullRunAt;
                    // Age matters: a stale green panel is the same false confidence this tool
                    // exists to remove.
                    GUILayout.Label($"checked {FormatAge(age)} ago", EditorStyles.miniLabel);
                }
                else GUILayout.Label("not checked yet", EditorStyles.miniLabel);
            }

            EditorGUILayout.Space(4);
            foreach (var c in _w.Setup.Checks) DrawRow(c);

            EditorGUILayout.Space(8);
            DrawConfluenceControls();
            EditorGUILayout.Space(8);
            DrawEffectiveEnvironment();
            EditorGUILayout.Space(8);
            DrawManualSteps();
        }

        void DrawRow(SetupCheck c)
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Label(PaytableToolsWindow.StatusIcon(c.Status), GUILayout.Width(20),
                        GUILayout.Height(18));
                    GUILayout.Label(c.Title, EditorStyles.boldLabel, GUILayout.Width(150));
                    GUILayout.Label(c.Summary ?? "—", EditorStyles.label);
                    GUILayout.FlexibleSpace();
                    if (c.HasFix)
                    {
                        using (new EditorGUI.DisabledScope(_w.Busy))
                        {
                            if (GUILayout.Button(c.FixLabel, GUILayout.Width(130)))
                            {
                                if (!c.FixWritesOutsideProject || ConfirmOutsideWrite(c))
                                    c.Fix();
                            }
                        }
                    }
                    if (c.Recheck != null)
                    {
                        using (new EditorGUI.DisabledScope(_w.Busy))
                        {
                            if (GUILayout.Button("↻", GUILayout.Width(24))) c.Recheck();
                        }
                    }
                }
                if (!string.IsNullOrEmpty(c.Detail))
                {
                    c.DetailExpanded = EditorGUILayout.Foldout(c.DetailExpanded, "details", true);
                    if (c.DetailExpanded)
                        EditorGUILayout.SelectableLabel(c.Detail, EditorStyles.wordWrappedMiniLabel,
                            GUILayout.Height(Mathf.Min(220, 14 * (c.Detail.Split('\n').Length + 1))));
                }
            }
        }

        static bool ConfirmOutsideWrite(SetupCheck c)
        {
            var target = c.Id == CheckId.Python ? PaytablePaths.VenvDir : SkillsTargetDirStatic();
            return EditorUtility.DisplayDialog(
                "Write outside the project?",
                $"{c.Title}\n\nThis writes to:\n{target}\n\nThat is outside the Unity project.",
                "Continue", "Cancel");
        }

        static string SkillsTargetDirStatic() => PaytablePaths.ProjectSkillsDir;

        string SkillsTargetDir =>
            _installUserWide ? PaytablePaths.UserSkillsDir : PaytablePaths.ProjectSkillsDir;

        static string FormatAge(double s) =>
            s < 60 ? $"{s:F0}s" : s < 3600 ? $"{s / 60:F0} min" : $"{s / 3600:F1} h";

        // ── checks ──────────────────────────────────────────────────────────

        public void RunAllChecks()
        {
            CheckPackage();
            CheckSkills();
            CheckUnityMcp();
            RunEnvDoctor();      // fills Python + Confluence when it finishes
            _w.Setup.LastFullRunAt = EditorApplication.timeSinceStartup;
        }

        void CheckPackage()
        {
            var c = _w.Setup.Get(CheckId.Package);
            var info = PaytablePaths.Info;
            if (info == null)
            {
                c.Set(CheckStatus.Blocked, "package not resolved",
                      "PackageInfo.FindForAssembly returned null. If the package IS in the project, " +
                      "an unrelated compile error may be blocking assembly load — check the console " +
                      "before suspecting this package.");
                return;
            }

            var smoke = "";
            try
            {
                var dist = PaytableGridMath.DistributeRows(5);
                smoke = string.Join(",", dist);
            }
            catch (Exception e) { smoke = "threw " + e.GetType().Name; }

            // The commit, not just the version: package.json's version does not change per
            // commit, so two people on wildly different code both read "0.2.0". Without the hash
            // a colleague cannot tell whether they are current, and neither can you from their
            // screenshot.
            var commit = PackageUpdate.ResolvedHash();
            var commitShort = commit.Length >= 12 ? commit.Substring(0, 12) : commit;

            var d = new StringBuilder()
                .AppendLine($"{info.name}@{info.version}")
                .AppendLine($"source:       {info.source}")
                .AppendLine($"commit:       {(commit.Length > 0 ? commitShort : "n/a (not a git source)")}")
                .AppendLine($"resolvedPath: {info.resolvedPath}")
                .AppendLine($"writable:     {PaytablePaths.PackageIsWritable}")
                .AppendLine($"Skills~:      {(Directory.Exists(PaytablePaths.SkillsSourceDir) ? "present" : "MISSING")}")
                .AppendLine($"DistributeRows(5) = {smoke}  (expected 3,2)");

            if (_update != null)
            {
                d.AppendLine();
                switch (_update.State)
                {
                    case PackageUpdate.State.UpToDate:
                        d.AppendLine($"Up to date with {_update.Url}" +
                                     (_update.Ref.Length > 0 ? " at " + _update.Ref : ""));
                        break;
                    case PackageUpdate.State.NoLocalPin:
                        d.AppendLine($"Remote is at {_update.RemoteShort}, but this project has no " +
                                     "pinned commit yet.");
                        d.AppendLine(_update.Message);
                        break;
                    case PackageUpdate.State.Behind:
                        d.AppendLine($"A newer commit exists: {_update.RemoteShort}");
                        d.AppendLine($"you have:              {_update.LocalShort}");
                        d.AppendLine();
                        d.AppendLine("Updating removes the pin from packages-lock.json (backing it up)");
                        d.AppendLine("and drops the cached copy, then asks Unity to resolve. Unity will");
                        d.AppendLine("reload afterwards. Client.Resolve() on its own would just restore");
                        d.AppendLine("the pin, which is why a plain refresh never picks up new commits.");
                        break;
                    default:
                        d.AppendLine(_update.Message);
                        break;
                }
            }

            var detail = d.ToString();
            var versionLine = $"{info.version}" +
                              (commitShort.Length > 0 ? $" ({commitShort})" : "") +
                              $", source {info.source}";

            if (!Directory.Exists(PaytablePaths.SkillsSourceDir))
                c.Set(CheckStatus.Wrong, "Skills~ missing from the package", detail);
            else if (smoke != "3,2")
                c.Set(CheckStatus.Wrong, "library C# did not load correctly", detail);
            else if (_update != null && _update.State == PackageUpdate.State.Behind)
                c.Set(CheckStatus.Warning, versionLine + " — update available", detail);
            else
                c.Set(CheckStatus.Ok, versionLine, detail);

            c.Recheck = CheckPackage;
            c.FixWritesOutsideProject = false;
            if (_update != null && _update.State == PackageUpdate.State.Behind)
            {
                c.FixLabel = "Update";
                c.Fix = ApplyUpdate;
            }
            else if (PaytablePaths.Source == UnityEditor.PackageManager.PackageSource.Git)
            {
                c.FixLabel = "Check for updates";
                c.Fix = CheckForUpdate;
            }
            else { c.FixLabel = null; c.Fix = null; }
        }

        void CheckForUpdate()
        {
            PackageUpdate.StartCheck(_w, r => { _update = r; CheckPackage(); _w.Repaint(); });
        }

        void ApplyUpdate()
        {
            if (!EditorUtility.DisplayDialog(
                    "Update the package?",
                    $"Fetch {_update.RemoteShort} in place of {_update.LocalShort}.\n\n" +
                    "This edits Packages/packages-lock.json (keeping a .bak), deletes the cached\n" +
                    "copy under Library/PackageCache, and reloads the editor.",
                    "Update", "Cancel"))
                return;

            if (PackageUpdate.Apply(out var report))
            {
                _update = null;
                Debug.Log("[Paytable Tool] " + report);
            }
            else Debug.LogError("[Paytable Tool] " + report);
        }

        void CheckUnityMcp()
        {
            var c = _w.Setup.Get(CheckId.UnityMcp);
            var found = UnityEditor.PackageManager.PackageInfo.GetAllRegisteredPackages()
                .FirstOrDefault(p => p.name == "com.coplaydev.unity-mcp");
            var embedded = Directory.Exists(Path.Combine(
                PaytablePaths.UnityProjectRoot ?? "", "Packages", "com.coplaydev.unity-mcp"));

            if (found != null || embedded)
                c.Set(CheckStatus.Ok, "installed",
                      "The assembly phase of paytable-verstka drives Unity through this bridge.\n" +
                      "Whether the bridge is actually connected can only be confirmed by the agent " +
                      "talking to it — this row only checks that the package is present.");
            else
                c.Set(CheckStatus.Warning, "not found",
                      "Phases 1-4 (extraction, atlas) work without it. Phase 5 onward — assembling " +
                      "the prefab — needs it. Installing it is outside this repo's scope.");
            c.Recheck = CheckUnityMcp;
        }

        void RunEnvDoctor()
        {
            var py = _w.Setup.Get(CheckId.Python);
            var conf = _w.Setup.Get(CheckId.Confluence);
            py.Set(CheckStatus.Checking, "probing…");
            conf.Set(CheckStatus.Checking, "probing…");

            var script = PaytablePaths.EnvDoctorScript;
            if (script == null)
            {
                py.Set(CheckStatus.Blocked, "env_doctor.py not found in the package");
                conf.Set(CheckStatus.Blocked, "env_doctor.py not found in the package");
                return;
            }
            // Prefer the venv when it exists: the doctor is stdlib-only so it runs anywhere, but
            // under the venv it can borrow certifi (a requests dependency) to verify TLS. macOS
            // python.org builds ship without a wired-up CA store, so a system interpreter fails the
            // live token check with CERTIFICATE_VERIFY_FAILED and nothing else can be concluded.
            // Falls back to a system interpreter when there is no venv yet — which is exactly the
            // case the doctor has to survive.
            var python = ExecutableLocator.FindPython(PaytablePaths.VenvExists);
            if (python == null)
            {
                py.Set(CheckStatus.Blocked, "no Python interpreter found",
                       "Looked in the framework, homebrew, /usr/local, pyenv versions and the " +
                       "login shell PATH. Unity does not inherit a login PATH, so a python3 that " +
                       "works in your terminal can still be invisible here.");
                return;
            }

            _w.StartProcess(python, new[] { script, "--kv" }, null, 120, p => ParseEnvDoctor(p));
        }

        void ParseEnvDoctor(ProcessProbe p)
        {
            var py = _w.Setup.Get(CheckId.Python);
            var conf = _w.Setup.Get(CheckId.Confluence);

            if (p.Status != ProcessProbe.State.Exited || p.ExitCode != 0)
            {
                var why = p.Status == ProcessProbe.State.TimedOut ? "timed out" : $"exit {p.ExitCode}";
                py.Set(CheckStatus.Blocked, "probe failed (" + why + ")", p.OutputText);
                conf.Set(CheckStatus.Blocked, "probe failed (" + why + ")", p.OutputText);
                return;
            }

            _env = new Dictionary<string, string>();
            _envLines = new List<string>();
            foreach (var line in p.StdOutText.Split('\n'))
            {
                var t = line.Trim();
                if (t.Length == 0) continue;
                var i = t.IndexOf('=');
                if (i <= 0) continue;
                var k = t.Substring(0, i);
                var v = t.Substring(i + 1);
                _envLines.Add(t);
                if (!_env.ContainsKey(k)) _env[k] = v;   // first wins; repeats are list entries
            }

            ApplyPython(py);
            ApplyConfluence(conf);
            _w.Setup.LastFullRunAt = EditorApplication.timeSinceStartup;
        }

        string Get(string k, string def = "") => _env.TryGetValue(k, out var v) ? v : def;
        IEnumerable<string> All(string k) =>
            _envLines.Where(l => l.StartsWith(k + "=")).Select(l => l.Substring(k.Length + 1));

        void ApplyPython(SetupCheck c)
        {
            var venvExists = Get("venv.exists") == "1";
            var missing = Get("deps.missing").Split(',').Where(s => s.Length > 0).ToArray();
            var shadowed = Get("deps.shadowed").Split(',').Where(s => s.Length > 0).ToArray();
            var okCount = Get("deps.ok", "0");
            var total = Get("deps.total", "5");

            var d = new StringBuilder();
            d.AppendLine($"venv: {Get("venv.python")}  {(venvExists ? "present" : "MISSING")}");
            d.AppendLine($"dependencies: {okCount}/{total} importable");
            if (missing.Length > 0) d.AppendLine("  missing:  " + string.Join(", ", missing));
            if (shadowed.Length > 0)
            {
                d.AppendLine("  shadowed: " + string.Join(", ", shadowed));
                d.AppendLine("  (importable, but resolved from OUTSIDE the venv — an unversioned");
                d.AppendLine("   copy is winning on sys.path, so what runs is not what you installed)");
            }
            d.AppendLine();
            d.AppendLine("usable base interpreters:");
            foreach (var i in All("interp.candidate")) d.AppendLine("  " + i.Replace("|", "  "));
            foreach (var i in All("interp.rejected")) d.AppendLine("  rejected: " + i.Replace("|", "  "));
            if (Get("python_extra_present") == "1")
            {
                d.AppendLine();
                d.AppendLine("~/.local/lib/python-extra exists. It is unversioned and used to be placed");
                d.AppendLine("ahead of the venv on sys.path. Remove it once the venv is healthy.");
            }

            c.FixWritesOutsideProject = true;
            c.Detail = d.ToString();
            c.Recheck = RunEnvDoctor;

            if (!venvExists)
            {
                c.Set(CheckStatus.Missing, "venv not created", c.Detail);
                c.FixLabel = "Create venv";
                c.Fix = CreateVenv;
            }
            else if (missing.Length > 0)
            {
                c.Set(CheckStatus.Wrong, $"{missing.Length} package(s) missing: {string.Join(", ", missing)}",
                      c.Detail);
                c.FixLabel = "Install missing";
                c.Fix = InstallRequirements;
            }
            else if (shadowed.Length > 0)
            {
                c.Set(CheckStatus.Warning, $"shadowed: {string.Join(", ", shadowed)}", c.Detail);
                c.FixLabel = null;
                c.Fix = null;
            }
            else
            {
                c.Set(CheckStatus.Ok, $"{okCount}/{total} packages, all inside the venv", c.Detail);
                c.FixLabel = "Reinstall";
                c.Fix = InstallRequirements;
            }
        }

        void ApplyConfluence(SetupCheck c)
        {
            var patExists = Get("confluence.pat_exists") == "1";
            var emailSet = Get("confluence.email_set") == "1";
            var tokenState = Get("confluence.token_state");

            var d = new StringBuilder();
            d.AppendLine($"site: {Get("confluence.base_url")}");
            d.AppendLine($"config file: {Get("config.file")}" +
                         (Get("config.exists") == "1" ? "" : "   (not written yet)"));
            d.AppendLine($"token file: {(patExists ? "present, mode " + Get("confluence.pat_mode") : "absent")}");
            d.AppendLine($"CONFLUENCE_EMAIL: {(emailSet ? "set  [from " + Get("confluence.email_source") + "]" : "NOT SET")}");
            d.AppendLine();

            switch (tokenState)
            {
                case "ok":
                    d.AppendLine($"Token: VALID — authenticated as {Get("confluence.token_account")}");
                    d.AppendLine();
                    d.AppendLine("This is the only credential the pipeline needs. A token fetches the page");
                    d.AppendLine("text, the attachment list AND the images — the image download redirects to");
                    d.AppendLine("a pre-signed media URL that needs no credential of its own. Browser cookie");
                    d.AppendLine("auth used to be the primary path and is gone: it bought nothing and cost a");
                    d.AppendLine("macOS Keychain prompt, a native dependency, and Chrome-only support.");
                    break;
                case "rejected":
                    d.AppendLine($"Token: REJECTED (HTTP {Get("confluence.token_http")}).");
                    d.AppendLine();
                    d.AppendLine("HTTP 401 means the credentials were parsed and turned down — expired,");
                    d.AppendLine("revoked, or issued under a different account than CONFLUENCE_EMAIL. Tokens");
                    d.AppendLine("now carry an expiry date, and an expired one looks exactly like a working");
                    d.AppendLine("one on disk.");
                    d.AppendLine();
                    d.AppendLine("Create a new one — the plain \"Create API token\", NOT \"with scopes\" — at");
                    d.AppendLine("id.atlassian.com/manage-profile/security/api-tokens, and paste it below.");
                    d.AppendLine("Scoped tokens go through api.atlassian.com/ex/confluence/<cloudId> instead");
                    d.AppendLine("of the site URL, which this pipeline does not use.");
                    break;
                case "no_email":
                    d.AppendLine("Token present, but CONFLUENCE_EMAIL is not set. Basic auth needs both.");
                    break;
                case "absent":
                    d.AppendLine("No token file. Nothing can authenticate without it.");
                    break;
                case "tls_untrusted":
                    d.AppendLine("Could not verify the token: this Python cannot validate TLS");
                    d.AppendLine("certificates, so the request never reached Confluence.");
                    d.AppendLine();
                    d.AppendLine("Not a network problem and not a token problem. macOS python.org");
                    d.AppendLine("builds ship without a wired-up CA store; `requests` never trips over");
                    d.AppendLine("it because it bundles certifi. Build the venv (which installs");
                    d.AppendLine("requests, and certifi with it) and this resolves itself.");
                    if (Get("confluence.token_detail").Length > 0)
                        d.AppendLine().AppendLine("  " + Get("confluence.token_detail"));
                    break;
                case "unreachable":
                    d.AppendLine("Could not reach Confluence to verify the token — offline?");
                    d.AppendLine("Offline is not the same as unauthorised, so this is not counted as failure.");
                    if (Get("confluence.token_detail").Length > 0)
                        d.AppendLine().AppendLine("  " + Get("confluence.token_detail"));
                    break;
                default:
                    d.AppendLine("Token: " + tokenState);
                    break;
            }

            c.Detail = d.ToString();
            c.Recheck = RunEnvDoctor;
            c.FixLabel = null;
            c.Fix = null;

            switch (tokenState)
            {
                case "ok":
                    c.Set(CheckStatus.Ok, "token valid — " + Get("confluence.token_account"), c.Detail);
                    break;
                case "rejected":
                    c.Set(CheckStatus.Wrong,
                          $"token rejected (HTTP {Get("confluence.token_http")}) — expired or revoked",
                          c.Detail);
                    break;
                case "tls_untrusted":
                    // Blocked, not Wrong: the token may well be fine, we could not ask.
                    c.Set(CheckStatus.Blocked,
                          "could not verify — this Python cannot validate TLS certificates", c.Detail);
                    break;
                case "unreachable":
                    // Blocked, never Ok and never a failure: we simply do not know.
                    c.Set(CheckStatus.Blocked, "offline — could not verify the token", c.Detail);
                    break;
                case "no_email":
                    c.Set(CheckStatus.Wrong, "token present without CONFLUENCE_EMAIL", c.Detail);
                    break;
                default:
                    c.Set(CheckStatus.Missing, "no API token configured", c.Detail);
                    break;
            }
        }

        void CheckSkills()
        {
            var c = _w.Setup.Get(CheckId.Skills);
            var src = PaytablePaths.SkillsSourceDir;
            var dst = SkillsTargetDir;
            c.Recheck = CheckSkills;
            c.FixWritesOutsideProject = _installUserWide;

            if (src == null || !Directory.Exists(src))
            {
                c.Set(CheckStatus.Blocked, "no Skills~ in the package");
                return;
            }

            var d = new StringBuilder();
            d.AppendLine("source: " + src);
            d.AppendLine("target: " + dst);
            d.AppendLine();

            int installed = 0, drifted = 0, absent = 0, linked = 0, badFrontmatter = 0;
            foreach (var name in PaytablePaths.SkillNames)
            {
                var s = Path.Combine(src, name);
                var t = Path.Combine(dst ?? "", name);
                if (!Directory.Exists(t)) { absent++; d.AppendLine($"  {name,-20} not installed"); continue; }

                var isLink = IsReparsePoint(t);
                var fmOk = HasValidFrontmatter(Path.Combine(t, "SKILL.md"), name, out var fmWhy);
                if (!fmOk) badFrontmatter++;

                if (isLink)
                {
                    linked++;
                    d.AppendLine($"  {name,-20} symlink → {ResolveLink(t)}{(fmOk ? "" : "  [" + fmWhy + "]")}");
                    continue;
                }
                var same = TreeHash(s) == TreeHash(t);
                if (same) installed++; else drifted++;
                d.AppendLine($"  {name,-20} {(same ? "up to date" : "DIFFERS from the package")}" +
                             (fmOk ? "" : "  [" + fmWhy + "]"));
            }

            if (badFrontmatter > 0)
            {
                d.AppendLine();
                d.AppendLine("A skill whose SKILL.md frontmatter is malformed is silently ignored by");
                d.AppendLine("Claude Code while looking perfectly installed — content identity alone");
                d.AppendLine("would call it fine.");
            }

            c.Detail = d.ToString();
            c.FixLabel = "Install / update";
            c.Fix = InstallSkills;

            if (badFrontmatter > 0) c.Set(CheckStatus.Wrong, $"{badFrontmatter} skill(s) with bad frontmatter", c.Detail);
            else if (absent > 0) c.Set(CheckStatus.Missing, $"{absent} of {PaytablePaths.SkillNames.Length} not installed", c.Detail);
            else if (drifted > 0) c.Set(CheckStatus.Warning, $"{drifted} differ(s) from the package", c.Detail);
            else if (linked > 0) c.Set(CheckStatus.Ok, $"{linked} symlinked (developer mode), {installed} copied", c.Detail);
            else c.Set(CheckStatus.Ok, "all up to date", c.Detail);
        }

        // ── fixes ───────────────────────────────────────────────────────────

        void CreateVenv()
        {
            var candidates = All("interp.candidate").Select(l => l.Split('|')).ToList();
            if (candidates.Count == 0)
            {
                EditorUtility.DisplayDialog("No usable interpreter",
                    "No Python 3.10+ interpreter was found that can build a venv.", "OK");
                return;
            }
            var basePython = candidates[0][1];
            _w.StartProcess(basePython, new[] { "-m", "venv", PaytablePaths.VenvDir }, null, 180,
                p =>
                {
                    if (p.Status == ProcessProbe.State.Exited && p.ExitCode == 0) InstallRequirements();
                    else RunEnvDoctor();
                });
        }

        void InstallRequirements()
        {
            var req = PaytablePaths.RequirementsFile;
            var args = new List<string>
            {
                "-m", "pip", "install", "--disable-pip-version-check", "--no-input",
                "--require-virtualenv",
                // Without a wheel, pip falls into a source build that runs for minutes and dies in
                // compiler output. This turns that into an immediate, readable failure.
                "--only-binary=:all:",
            };
            if (req != null) { args.Add("-r"); args.Add(req); }
            else
            {
                // A git-resolved package has only library/, so requirements.txt is not on disk.
                args.AddRange(new[] { "requests", "pillow", "numpy", "scipy" });
            }
            _w.StartProcess(PaytablePaths.VenvPython, args, null, 900, _ => RunEnvDoctor());
        }

        void InstallSkills()
        {
            var src = PaytablePaths.SkillsSourceDir;
            var dst = SkillsTargetDir;
            if (src == null || dst == null) return;

            var relinked = new List<string>();
            foreach (var name in PaytablePaths.SkillNames)
            {
                var t = Path.Combine(dst, name);
                // Never silently replace a symlink: that is a developer's live setup, and eating it
                // would look like the tool losing their work.
                if (Directory.Exists(t) && IsReparsePoint(t))
                {
                    if (!EditorUtility.DisplayDialog("Replace a symlink?",
                            $"{t}\n\ncurrently points at\n{ResolveLink(t)}\n\n" +
                            "Replacing it with a copy ends developer mode for this skill.",
                            "Replace", "Skip"))
                        continue;
                    Directory.Delete(t);
                    relinked.Add(name);
                }
                CopyTree(Path.Combine(src, name), t);
            }
            AssetDatabase.Refresh();
            CheckSkills();
        }

        // ── confluence controls ─────────────────────────────────────────────

        void DrawConfluenceControls()
        {
            EditorGUILayout.LabelField("Confluence settings", EditorStyles.boldLabel);
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                _confluenceEmail = EditorGUILayout.TextField("CONFLUENCE_EMAIL", _confluenceEmail);

                EditorGUILayout.LabelField(
                    "API token — required, this is the only credential", EditorStyles.miniLabel);
                _patInput = EditorGUILayout.PasswordField("API token", _patInput);
                EditorGUILayout.LabelField(
                    PaytablePaths.IsWindows
                        ? "Stored at ~/.confluence_pat. Note: file permissions are NOT restricted on Windows."
                        : "Stored at ~/.confluence_pat with mode 600. Never shown again once written.",
                    EditorStyles.miniLabel);

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Save settings", GUILayout.Width(120))) SaveConfluenceSettings();
                    GUILayout.FlexibleSpace();
                    var wide = GUILayout.Toggle(_installUserWide,
                        "install skills user-wide (~/.claude/skills)");
                    if (wide != _installUserWide)
                    {
                        // Persisted immediately and on its own. It used to ride along with
                        // "Save settings", which meant toggling a checkbox forced a write of every
                        // other field — including an empty email over a working one.
                        _installUserWide = wide;
                        EditorPrefs.SetBool("CGS.Paytable.Setup.InstallUserWide", _installUserWide);
                        CheckSkills();
                    }
                }
            }
        }

        void SaveConfluenceSettings()
        {
            EditorPrefs.SetBool("CGS.Paytable.Setup.InstallUserWide", _installUserWide);

            // Non-secret settings go to the config file the Python scripts read. Never ~/.zshrc:
            // shell-specific, useless on Windows, and invisible to a GUI-launched Unity.
            //
            // MERGE, never replace, and never write an empty value over a stored one. Replacing
            // wholesale is how pressing this button once wiped a working CONFLUENCE_EMAIL: the
            // in-memory field was blank after a domain reload, and the blank won.
            var email = _confluenceEmail;
            if (string.IsNullOrWhiteSpace(email)) email = ReadConfig("CONFLUENCE_EMAIL");
            if (string.IsNullOrWhiteSpace(email))
            {
                Debug.LogWarning("[Paytable Tool] CONFLUENCE_EMAIL is empty — leaving the stored " +
                                 "value alone rather than clearing it.");
            }
            else
            {
                EditorPrefs.SetString("CGS.Paytable.Setup.ConfluenceEmail", email);
                _confluenceEmail = email;
                var cfg = PaytablePaths.ToolsConfigFile;
                if (cfg != null)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(cfg));
                    File.WriteAllText(cfg, "{\n  \"CONFLUENCE_EMAIL\": " + Esc(email) + "\n}\n");
                }
            }

            if (!string.IsNullOrEmpty(_patInput))
            {
                File.WriteAllText(PaytablePaths.ConfluencePatFile, _patInput.Trim());
                RestrictPatPermissions();
                _patInput = "";
            }
            RunEnvDoctor();
        }

        /// <summary>
        /// chmod 600 on the token file, run synchronously.
        ///
        /// This is the one place that blocks, and deliberately: it finishes in milliseconds, and
        /// routing it through the shared async probe would occupy the very slot RunEnvDoctor needs
        /// on the next line — the re-check would be dropped as "already running" and the row would
        /// sit on a stale result. Windows has no equivalent; the UI says so rather than implying
        /// the file is protected there.
        /// </summary>
        static void RestrictPatPermissions()
        {
            if (PaytablePaths.IsWindows) return;
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "/bin/chmod",
                    Arguments = "600 " + ProcessProbe.Quote(PaytablePaths.ConfluencePatFile),
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                using (var p = System.Diagnostics.Process.Start(psi))
                {
                    if (p != null && !p.WaitForExit(5000)) { try { p.Kill(); } catch { } }
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Paytable Tool] could not chmod the token file: " + e.Message);
            }
        }

        static string Esc(string s) =>
            "\"" + (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

        // ── informational panels ────────────────────────────────────────────

        void DrawEffectiveEnvironment()
        {
            EditorGUILayout.LabelField("Effective settings", EditorStyles.boldLabel);
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                Row("venv python", PaytablePaths.VenvPython + (PaytablePaths.VenvExists ? "" : "  (missing)"));
                Row("config file", PaytablePaths.ToolsConfigFile);
                Row("skills target", SkillsTargetDir);
                Row("package", PaytablePaths.PackageRoot);
                Row("git repo root", PaytablePaths.GitRepoRoot);
            }
        }

        static void Row(string k, string v)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label(k, EditorStyles.miniLabel, GUILayout.Width(110));
                EditorGUILayout.SelectableLabel(v ?? "—", EditorStyles.miniLabel, GUILayout.Height(14));
            }
        }

        void DrawManualSteps()
        {
            EditorGUILayout.LabelField("Steps only you can do", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "These never turn green on their own — that is not a bug:\n\n" +
                "• Create the API token yourself at id.atlassian.com — the plain kind, not \"with " +
                "scopes\" — and give it the longest expiry offered. Tokens expire, and an expired one " +
                "looks identical to a working one on disk; the row above is what catches it.\n" +
                "• Repo access on GitHub is granted by someone with admin, and `gh auth login` is a " +
                "browser flow.\n" +
                "• Installing the unityMCP server itself is outside this repo's scope.",
                MessageType.None);
        }

        // ── filesystem helpers ──────────────────────────────────────────────

        static bool IsReparsePoint(string path)
        {
            try
            {
                return (new DirectoryInfo(path).Attributes & FileAttributes.ReparsePoint) != 0;
            }
            catch { return false; }
        }

        static string ResolveLink(string path)
        {
            try
            {
                var fi = new DirectoryInfo(path);
                // Good enough for display; the content hash is what actually decides drift.
                return fi.FullName;
            }
            catch { return path; }
        }

        /// <summary>
        /// Content identity, normalised for line endings — a Windows checkout with
        /// core.autocrlf=true would otherwise report permanent drift.
        /// </summary>
        static string TreeHash(string dir)
        {
            if (!Directory.Exists(dir)) return "";
            using (var sha = System.Security.Cryptography.SHA256.Create())
            {
                var files = Directory.GetFiles(dir, "*", SearchOption.AllDirectories)
                    .Where(f => !f.Contains("__pycache__") && !f.EndsWith(".pyc")
                                && !f.EndsWith(".DS_Store"))
                    .Select(f => new { Rel = f.Substring(dir.Length).Replace('\\', '/'), Full = f })
                    .OrderBy(x => x.Rel, StringComparer.Ordinal)
                    .ToList();
                var acc = new StringBuilder();
                foreach (var f in files)
                {
                    acc.Append(f.Rel).Append('\n');
                    try
                    {
                        var bytes = File.ReadAllBytes(f.Full);
                        var text = Encoding.UTF8.GetString(bytes).Replace("\r\n", "\n");
                        acc.Append(Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes(text))));
                    }
                    catch { acc.Append("unreadable"); }
                    acc.Append('\n');
                }
                return Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes(acc.ToString())));
            }
        }

        static bool HasValidFrontmatter(string skillMd, string expectedName, out string why)
        {
            why = null;
            if (!File.Exists(skillMd)) { why = "no SKILL.md"; return false; }
            string text;
            try { text = File.ReadAllText(skillMd); }
            catch (Exception e) { why = e.GetType().Name; return false; }
            if (!text.StartsWith("---")) { why = "no frontmatter block"; return false; }
            var end = text.IndexOf("\n---", 3, StringComparison.Ordinal);
            if (end < 0) { why = "frontmatter not terminated"; return false; }
            var fm = text.Substring(3, end - 3);
            if (fm.IndexOf("name:", StringComparison.Ordinal) < 0) { why = "no name:"; return false; }
            if (fm.IndexOf("description:", StringComparison.Ordinal) < 0)
            {
                why = "no description: — the skill loads but can only be invoked by exact name";
                return false;
            }
            return true;
        }

        static void CopyTree(string src, string dst)
        {
            Directory.CreateDirectory(dst);
            foreach (var dir in Directory.GetDirectories(src, "*", SearchOption.AllDirectories))
            {
                if (dir.Contains("__pycache__")) continue;
                Directory.CreateDirectory(dir.Replace(src, dst));
            }
            foreach (var file in Directory.GetFiles(src, "*", SearchOption.AllDirectories))
            {
                if (file.Contains("__pycache__") || file.EndsWith(".pyc")) continue;
                var target = file.Replace(src, dst);
                Directory.CreateDirectory(Path.GetDirectoryName(target));
                File.Copy(file, target, true);
            }
        }
    }
}

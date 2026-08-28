using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace CGS.PaytableLibrary.Tooling
{
    /// <summary>
    /// The only place that knows where anything lives. Every other file asks this one.
    ///
    /// The package reaches a project three different ways and they do NOT behave alike, so nothing
    /// here may assume the package is writable or that its path is stable — see <see cref="Source"/>.
    /// </summary>
    public static class PaytablePaths
    {
        public const string PackageName = "com.cgs.paytablelibrary";

        /// <summary>Home directory. Not $HOME — that is undefined on Windows.</summary>
        public static string Home => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        public static bool IsWindows => Application.platform == RuntimePlatform.WindowsEditor;

        /// <summary>
        /// Where this package actually resolved to. Never build this from a literal path: a
        /// git-resolved package lives under Library/PackageCache/ with a hash suffix that changes
        /// on every re-resolve.
        /// </summary>
        public static string PackageRoot
        {
            get
            {
                var info = PackageInfo.FindForAssembly(typeof(PaytablePaths).Assembly);
                return info != null ? info.resolvedPath : null;
            }
        }

        public static PackageInfo Info => PackageInfo.FindForAssembly(typeof(PaytablePaths).Assembly);

        /// <summary>
        /// Git / Embedded / Local, and they differ in ways that matter:
        ///
        ///   Git      — read-only under Library/PackageCache/, wiped and re-fetched on every
        ///              re-resolve. Never write here; never symlink into here, because the link
        ///              dangles on the next resolve and the skill simply disappears.
        ///   Local    — a `file:` reference to a clone. Writable, edits are live.
        ///   Embedded — a copy or symlink inside Packages/. Behaves like Local.
        ///
        /// Most confused reports about this tool trace back to not knowing which one is active, so
        /// the window shows it at the top of the Setup tab.
        /// </summary>
        public static UnityEditor.PackageManager.PackageSource Source
        {
            get
            {
                var info = Info;
                return info != null ? info.source : UnityEditor.PackageManager.PackageSource.Unknown;
            }
        }

        public static bool PackageIsWritable =>
            Source != UnityEditor.PackageManager.PackageSource.Git;

        /// <summary>The three skills, as shipped inside the package. Unity ignores `~` folders.</summary>
        public static string SkillsSourceDir
        {
            get
            {
                var root = PackageRoot;
                return string.IsNullOrEmpty(root) ? null : Path.Combine(root, "Skills~");
            }
        }

        public static readonly string[] SkillNames =
        {
            "paytable-verstka", "paytable-pipeline", "cgs-atlas-builder"
        };

        /// <summary>The Unity project folder (the one containing Assets/).</summary>
        public static string UnityProjectRoot =>
            Directory.GetParent(Application.dataPath)?.FullName;

        /// <summary>
        /// The git repo root, which is NOT the Unity project root — this project lives at
        /// &lt;repo&gt;/Client/Konami-Slots. Claude Code sessions open at the repo root, so that is
        /// where its .claude/ directory is and where project-level skills belong.
        /// Walks up looking for .git; falls back to the Unity project root.
        /// </summary>
        public static string GitRepoRoot
        {
            get
            {
                var dir = new DirectoryInfo(UnityProjectRoot ?? ".");
                while (dir != null)
                {
                    if (Directory.Exists(Path.Combine(dir.FullName, ".git")) ||
                        File.Exists(Path.Combine(dir.FullName, ".git")))
                        return dir.FullName;
                    dir = dir.Parent;
                }
                return UnityProjectRoot;
            }
        }

        /// <summary>Where the window installs skills: project-scoped, so `~` stays untouched.</summary>
        public static string ProjectSkillsDir
        {
            get
            {
                var root = GitRepoRoot;
                return string.IsNullOrEmpty(root) ? null : Path.Combine(root, ".claude", "skills");
            }
        }

        /// <summary>The user-wide alternative, for machines where a project-level install is wrong.</summary>
        public static string UserSkillsDir => Path.Combine(Home, ".claude", "skills");

        // ── Python ──────────────────────────────────────────────────────────
        // Fixed absolute location, matching what _bootstrap.py looks for. Deliberately NOT relative
        // to the repo: once skills are COPIED into another repo's .claude/skills/, __file__ no
        // longer sits under the paytable repo and a repo-relative venv cannot be found.

        public static string VenvDir => Path.Combine(Home, ".venvs", "paytable-tools");

        public static string VenvPython => IsWindows
            ? Path.Combine(VenvDir, "Scripts", "python.exe")
            : Path.Combine(VenvDir, "bin", "python");

        public static bool VenvExists => File.Exists(VenvPython);

        public static string RequirementsFile
        {
            get
            {
                var root = PackageRoot;
                if (string.IsNullOrEmpty(root)) return null;
                // requirements.txt lives at the REPO root, one level above the package. Present
                // for Local/Embedded sources; absent for Git, where only library/ was fetched.
                var sibling = Path.Combine(Directory.GetParent(root)?.FullName ?? root, "requirements.txt");
                return File.Exists(sibling) ? sibling : null;
            }
        }

        // ── Confluence ──────────────────────────────────────────────────────

        public static string ConfluencePatFile => Path.Combine(Home, ".confluence_pat");

        /// <summary>Non-secret settings the Python scripts read directly. Never the token.</summary>
        public static string ToolsConfigFile
        {
            get
            {
                if (IsWindows)
                {
                    var appData = Environment.GetEnvironmentVariable("APPDATA");
                    return string.IsNullOrEmpty(appData)
                        ? null
                        : Path.Combine(appData, "paytable-tools", "config.json");
                }
                var xdg = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
                var baseDir = string.IsNullOrEmpty(xdg) ? Path.Combine(Home, ".config") : xdg;
                return Path.Combine(baseDir, "paytable-tools", "config.json");
            }
        }

        public static string EnvDoctorScript
        {
            get
            {
                var skills = SkillsSourceDir;
                if (string.IsNullOrEmpty(skills)) return null;
                var p = Path.Combine(skills, "paytable-pipeline", "scripts", "env_doctor.py");
                return File.Exists(p) ? p : null;
            }
        }
    }
}
